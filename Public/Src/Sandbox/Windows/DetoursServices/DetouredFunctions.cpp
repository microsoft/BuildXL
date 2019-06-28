// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "stdafx.h"
#include <algorithm>
#include <winternl.h>

#include "DebuggingHelpers.h"
#include "DetouredFunctions.h"
#include "DetoursHelpers.h"
#include "DetoursServices.h"
#include "FileAccessHelpers.h"
#include "DetouredScope.h"
#include "SendReport.h"
#include "StringOperations.h"
#include "UnicodeConverter.h"
#include "MetadataOverrides.h"
#include "HandleOverlay.h"
#include "SubstituteProcessExecution.h"

using std::wstring;
using std::unique_ptr;
using std::vector;

// ----------------------------------------------------------------------------
// FUNCTION DEFINITIONS
// ----------------------------------------------------------------------------

#define IMPLEMENTED(x) // bookeeping to remember which functions have been fully implemented and which still need to be done
#define RETRY_DETOURING_PROCESS_COUNT 5 // How many times to retry detouring a process.
#define DETOURS_STATUS_ACCESS_DENIED (NTSTATUS)0xC0000022L;
#define INITIAL_REPARSE_DATA_BUILDXL_DETOURS_BUFFER_SIZE_FOR_FILE_NAMES 1024
#define SYMLINK_FLAG_RELATIVE 0x00000001

/// <summary>
/// Checks if a file is a reparse point by calling <code>GetFileAttributesW</code>.
/// </summary>
static bool IsReparsePoint(_In_ LPCWSTR lpFileName)
{
    if (IgnoreReparsePoints())
    {
        return false;
    }

    DWORD lastError = GetLastError();
    DWORD attributes;
    bool result = lpFileName != nullptr
        && ((attributes = GetFileAttributesW(lpFileName)) != INVALID_FILE_ATTRIBUTES)
        && ((attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0);

    SetLastError(lastError);

    return result;
}

/// <summary>
/// Gets reparse point type of a file name by querying <code>dwReserved0</code> field of <code>WIN32_FIND_DATA</code>.
/// </summary>
static DWORD GetReparsePointType(_In_ LPCWSTR lpFileName)
{
    DWORD ret = 0;

    if (!IgnoreReparsePoints())
    {
        DWORD lastError = GetLastError();

        if (IsReparsePoint(lpFileName))
        {
            WIN32_FIND_DATA findData;

            HANDLE findDataHandle = FindFirstFileW(lpFileName, &findData);
            if (findDataHandle != INVALID_HANDLE_VALUE)
            {
                ret = findData.dwReserved0;
                FindClose(findDataHandle);
            }
        }

        SetLastError(lastError);
    }

    return ret;
}

/// <summary>
/// Checks if a reparse point type is actionable, i.e., it is either <code>IO_REPARSE_TAG_SYMLINK</code> or <code>IO_REPARSE_TAG_MOUNT_POINT</code>.
/// </summary>
static bool IsActionableReparsePointType(_In_ const DWORD reparsePointType)
{
    return reparsePointType == IO_REPARSE_TAG_SYMLINK || reparsePointType == IO_REPARSE_TAG_MOUNT_POINT;
}

/// <summary>
/// Gets the final full path by handle.
/// </summary>
/// <remarks>
/// This function encapsulates calls to <code>GetFinalPathNameByHandleW</code> and allocates memory as needed.
/// </remarks>
static DWORD DetourGetFinalPathByHandle(_In_ HANDLE hFile, _Inout_ wstring& fullPath)
{
    // First, we try with a fixed-sized buffer, which should be good enough for all practical cases.

    wchar_t wszBuffer[MAX_PATH];
    DWORD nBufferLength = std::extent<decltype(wszBuffer)>::value;

    DWORD result = GetFinalPathNameByHandleW(hFile, wszBuffer, nBufferLength, FILE_NAME_NORMALIZED);

    if (result == 0)
    {
        DWORD ret = GetLastError();
        return ret;
    }

    if (result < nBufferLength)
    {
        // The buffer was big enough. The return value indicates the length of the full path, NOT INCLUDING the terminating null character.
        // https://msdn.microsoft.com/en-us/library/windows/desktop/aa364962(v=vs.85).aspx
        fullPath.assign(wszBuffer, static_cast<size_t>(result));
    }
    else
    {
        // Second, if that buffer wasn't big enough, we try again with a dynamically allocated buffer with sufficient size.

        // Note that in this case, the return value indicates the required buffer length, INCLUDING the terminating null character.
        // https://msdn.microsoft.com/en-us/library/windows/desktop/aa364962(v=vs.85).aspx
        unique_ptr<wchar_t[]> buffer(new wchar_t[result]);
        assert(buffer.get());

        DWORD result2 = GetFinalPathNameByHandleW(hFile, buffer.get(), result, FILE_NAME_NORMALIZED);

        if (result2 == 0)
        {
            DWORD ret = GetLastError();
            return ret;
        }

        if (result2 < result)
        {
            fullPath.assign(buffer.get(), result2);
        }
        else
        {
            return ERROR_NOT_ENOUGH_MEMORY;
        }
    }

    return ERROR_SUCCESS;
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
/////////////////////////////////////////// Symlink traversal utilities /////////////////////////////////////////////////////////
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

/// <summary>
/// Split paths into path atoms and insert them into <code>atoms</code> in reverse order.
/// </summary>
static void SplitPathsReverse(_In_ const wstring& path, _Inout_ vector<wstring>& atoms)
{
    size_t length = path.length();

    if (length >= 2 && IsDirectorySeparator(path[length - 1]))
    {
        // Skip ending directory separator without trimming the path.
        --length;
    }

    size_t rootLength = GetRootLength(path.c_str());

    if (length <= rootLength)
    {
        return;
    }

    size_t i = length - 1;
    wstring dir = path;

    while (i >= rootLength)
    {
        while (i > rootLength && !IsDirectorySeparator(dir[i]))
        {
            --i;
        }

        if (i >= rootLength)
        {
            atoms.push_back(dir.substr(i));
        }

        dir = dir.substr(0, i);

        if (i == 0)
        {
            break;
        }

        --i;
    }

    if (!dir.empty())
    {
        atoms.push_back(dir);
    }
}

/// <summary>
/// Gets target name from <code>REPARSE_DATA_BUFFER</code>.
/// </summary>
static void GetTargetNameFromReparseData(_In_ PREPARSE_DATA_BUFFER pReparseDataBuffer, _In_ DWORD reparsePointType, _Out_ wstring& name)
{
    // In what follows, we first try to extract target name in the path buffer using the PrintNameOffset.
    // If it is empty or a single space, we try to extract target name from the SubstituteNameOffset.
    // This is pretty much guess-work. Tools like mklink and CreateSymbolicLink API insert the target name
    // from the PrintNameOffset. But others may use DeviceIoControl directly to insert the target name from SubstituteNameOffset.
    if (reparsePointType == IO_REPARSE_TAG_SYMLINK)
    {
        name.assign(
            pReparseDataBuffer->SymbolicLinkReparseBuffer.PathBuffer + pReparseDataBuffer->SymbolicLinkReparseBuffer.PrintNameOffset / sizeof(WCHAR),
            (size_t)pReparseDataBuffer->SymbolicLinkReparseBuffer.PrintNameLength / sizeof(WCHAR));

        if (name.size() == 0 || name == L" ")
        {
            name.assign(
                pReparseDataBuffer->SymbolicLinkReparseBuffer.PathBuffer + pReparseDataBuffer->SymbolicLinkReparseBuffer.SubstituteNameOffset / sizeof(WCHAR),
                (size_t)pReparseDataBuffer->SymbolicLinkReparseBuffer.SubstituteNameLength / sizeof(WCHAR));
        }
    }
    else if (reparsePointType == IO_REPARSE_TAG_MOUNT_POINT)
    {
        name.assign(
            pReparseDataBuffer->MountPointReparseBuffer.PathBuffer + pReparseDataBuffer->MountPointReparseBuffer.PrintNameOffset / sizeof(WCHAR),
            (size_t)pReparseDataBuffer->MountPointReparseBuffer.PrintNameLength / sizeof(WCHAR));

        if (name.size() == 0 || name == L" ")
        {
            name.assign(
                pReparseDataBuffer->MountPointReparseBuffer.PathBuffer + pReparseDataBuffer->MountPointReparseBuffer.SubstituteNameOffset / sizeof(WCHAR),
                (size_t)pReparseDataBuffer->MountPointReparseBuffer.SubstituteNameLength / sizeof(WCHAR));
        }
    }
}

/// <summary>
/// Gets the next symlink target of a path.
/// </summary>
static bool TryGetNextTarget(_In_ const wstring& path, _In_ HANDLE hInput, _Inout_ wstring& target)
{
    DWORD lastError = GetLastError();

    HANDLE hFile = hInput != INVALID_HANDLE_VALUE
        ? hInput
        : CreateFileW(
            path.c_str(),
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_DELETE | FILE_SHARE_WRITE,
            NULL,
            OPEN_EXISTING,
            FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS,
            NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        SetLastError(lastError);
        return false;
    }

    DWORD bufferSize = INITIAL_REPARSE_DATA_BUILDXL_DETOURS_BUFFER_SIZE_FOR_FILE_NAMES;
    DWORD errorCode = ERROR_INSUFFICIENT_BUFFER;
    DWORD bufferReturnedSize = 0;

    vector<char> buffer;
    while (errorCode == ERROR_MORE_DATA || errorCode == ERROR_INSUFFICIENT_BUFFER)
    {
        buffer.clear();
        buffer.resize(bufferSize);
        BOOL success = DeviceIoControl(
            hFile,
            FSCTL_GET_REPARSE_POINT,
            nullptr,
            0,
            buffer.data(),
            bufferSize,
            &bufferReturnedSize,
            nullptr);

        bufferSize *= 2;
        if (success)
        {
            errorCode = ERROR_SUCCESS;
        }
        else
        {
            errorCode = GetLastError();
        }
    }

    if (errorCode != ERROR_SUCCESS)
    {
        if (hFile != hInput)
        {
            CloseHandle(hFile);
        }

        SetLastError(lastError);

        return false;
    }

    PREPARSE_DATA_BUFFER pReparseDataBuffer = (PREPARSE_DATA_BUFFER)buffer.data();

    DWORD reparsePointType = pReparseDataBuffer->ReparseTag;

    if (!IsActionableReparsePointType(reparsePointType))
    {
        if (hFile != hInput)
        {
            CloseHandle(hFile);
        }

        SetLastError(lastError);

        return false;
    }

    GetTargetNameFromReparseData(pReparseDataBuffer, reparsePointType, target);

    if (hFile != hInput)
    {
        CloseHandle(hFile);
    }

    SetLastError(lastError);

    return true;
}

/// <summary>
/// Resolves a reparse point path with respect to its relative target.
/// </summary>
/// <remarks>
/// Given a reparse point path A\B\C and its relative target D\E\F, this method
/// simply "combines" A\B and D\E\F. The symlink C is essentially replaced by the relative target D\E\F.
/// </remarks>
static bool TryResolveRelativeTarget(
    _Inout_ wstring& result, 
    _In_ const wstring& relativeTarget, 
    _In_ vector<wstring> *processed, 
    _In_ vector<wstring> *needToBeProcessed)
{
    // Trim directory separator ending.
    if (result[result.length() - 1] == L'\\')
    {
        result = result.substr(0, result.length() - 1);
    }

    // Skip last path atom.
    size_t lastSeparator = result.find_last_of(L'\\');
    if (lastSeparator == std::string::npos)
    {
        return false;
    }

    if (processed != nullptr)
    {
        if (processed->empty())
        {
            return false;
        }

        processed->pop_back();
    }

    // Handle '.' and '..' in the relative target.
    size_t pos = 0;
    size_t length = relativeTarget.length();
    bool startWithDotSlash = length >= 2 && relativeTarget[pos] == L'.' && relativeTarget[pos + 1] == L'\\';
    bool startWithDotDotSlash = length >= 3 && relativeTarget[pos] == L'.' && relativeTarget[pos + 1] == L'.' && relativeTarget[pos + 2] == L'\\';

    while ((startWithDotDotSlash || startWithDotSlash) && lastSeparator != std::string::npos)
    {
        if (startWithDotSlash)
        {
            pos += 2;
            length -= 2;
        }
        else
        {
            pos += 3;
            length -= 3;
            lastSeparator = result.find_last_of(L'\\', lastSeparator - 1);
            if (processed != nullptr && !processed->empty())
            {
                if (processed->empty())
                {
                    return false;
                }

                processed->pop_back();
            }
        }

        startWithDotSlash = length >= 2 && relativeTarget[pos] == L'.' && relativeTarget[pos + 1] == L'\\';
        startWithDotDotSlash = length >= 3 && relativeTarget[pos] == L'.' && relativeTarget[pos + 1] == L'.' && relativeTarget[pos + 2] == L'\\';
    }

    if (lastSeparator == std::string::npos && startWithDotDotSlash)
    {
        return false;
    }

    wstring slicedTarget;
    slicedTarget.append(relativeTarget, pos, length);

    result = result.substr(0, lastSeparator != std::string::npos ? lastSeparator : 0);

    if (needToBeProcessed != nullptr)
    {
        SplitPathsReverse(slicedTarget, *needToBeProcessed);
    }
    else
    {
        result.push_back(L'\\');
        result.append(slicedTarget);
    }

    return true;
}

/// <summary>
/// Resolves the reparse points with relative target. 
/// </summary>
/// <remarks>
/// This method resolves reparse points that occur in the path prefix. This method should only be called when path itself
/// is an actionable reparse point whose target is a relative path. 
/// This method traverses each prefix starting from the shortest one. Every time it encounters a directory symlink, it uses GetFinalPathNameByHandle to get the final path. 
/// However, if the prefix itself is a junction, then it leaves the current resolved path intact. 
/// The following example show the needs for this method as a prerequisite in getting 
/// the immediate target of a reparse point. Suppose that we have the following file system layout:
///
///    repo
///    |
///    +---intermediate
///    |   \---current
///    |         symlink1.link ==> ..\..\target\file1.txt
///    |         symlink2.link ==> ..\target\file2.txt
///    |
///    +---source ==> intermediate\current (case 1: directory symlink, case 2: junction)
///    |
///    \---target
///          file1.txt
///          file2.txt
///
/// **CASE 1**: source ==> intermediate\current is a directory symlink. 
///
/// If a tool accesses repo\source\symlink1.link (say 'type repo\source\symlink1.link'), then the tool should get the content of repo\target\file1.txt.
/// If the tool accesses repo\source\symlink2.link, then the tool should get path-not-found error because the resolved path will be repo\intermediate\target\file2.txt.
/// Now, if we try to resolve repo\source\symlink1.link by simply combining it with ..\..\target\file1.txt, then we end up with target\file1.txt (not repo\target\file1.txt),
/// which is a non-existent path. To resolve repo\source\symlink1, we need to resolve the reparse points of its prefix, i.e., repo\source. For directory symlinks,
/// we need to resolve the prefix to its target. I.e., repo\source is resolved to repo\intermediate\current, and so, given repo\source\symlink1.link, this method returns
/// repo\intermediate\current\symlink1.link. Combining repo\intermediate\current\symlink1.link with ..\..\target\file1.txt will give the correct path, i.e., repo\target\file1.txt.
/// 
/// Similarly, given repo\source\symlink2.link, the method returns repo\intermediate\current\symlink2.link, and combining it with ..\target\file2.txt, will give us
/// repo\intermediate\target\file2.txt, which is a non-existent path. This corresponds to the behavior of symlink accesses above.
///
/// **CASE 2**: source ==> intermediate\current is a junction.
///
/// If a tool accesses repo\source\symlink1.link (say 'type repo\source\symlink1.link'), then the tool should get path-not-found error because the resolve path will be target\file1.txt (not repo\target\file1).
/// If the tool accesses repo\source\symlink2.link, then the tool should the content of repo\target\file2.txt.
/// Unlike directory symlinks, when we try to resolve repo\source\symlink2.link, the prefix repo\source is left intact because it is a junction. Thus, combining repo\source\symlink2.link
/// with ..\target\file2.txt results in a correct path, i.e., repo\target\file2.txt. The same reasoning can be given for repo\source\symlink1.link, and its resolution results in
/// a non-existent path target\file1.txt.
/// </remarks>
static bool TryResolveRelativeTarget(_In_ const wstring& path, _In_ const wstring& relativeTarget, _Inout_ wstring& result)
{
    vector<wstring> needToBeProcessed;
    vector<wstring> processed;

    // Split path into atoms that need to be processed one-by-one.
    // For example, C:\P1\P2\P3\symlink --> symlink, P3, P1, P2, C:
    SplitPathsReverse(path, needToBeProcessed);

    while (!needToBeProcessed.empty())
    {
        wstring atom = needToBeProcessed.back();
        needToBeProcessed.pop_back();
        processed.push_back(atom);

        if (!result.empty())
        {
            // Append directory separator as necessary.
            if (result[result.length() - 1] != L'\\' && atom[0] != L'\\')
            {
                result.append(L"\\");
            }
        }

        result.append(atom);

        if (needToBeProcessed.empty())
        {
            // The last atom is the symlink that we are going to replace.
            break;
        }

        if (GetReparsePointType(result.c_str()) == IO_REPARSE_TAG_SYMLINK)
        {
            // Prefix path is a directory symlink.
            // For example, C:\P1\P2 is a directory symlink.

            // Get the next target of the directory symlink.
            wstring target;
            if (!TryGetNextTarget(result, INVALID_HANDLE_VALUE, target))
            {
                return false;
            }

            if (GetRootLength(target.c_str()) > 0)
            {
                // The target of the directory symlink is a rooted path:
                // - clear result so far,
                // - restart all the processed atoms,
                // - initialize the atoms to be processed.
                result.clear();
                processed.clear();
                SplitPathsReverse(target, needToBeProcessed);
            }
            else
            {
                // The target of the directory symlink is a relative path, then resolve it by "combining"
                // the directory symlink (stored in the result) and the relative target.
                if (!TryResolveRelativeTarget(result, target, &processed, &needToBeProcessed))
                {
                    return false;
                }
            }
        }
    }

    // Finally, resolve the last atom, i.e., the symlink atom.
    if (!TryResolveRelativeTarget(result, relativeTarget, nullptr, nullptr))
    {
        return false;
    }

    return true;
}

/// <summary>
/// Get the next path of a reparse point path.
/// </summary>
static bool TryGetNextPath(_In_ const wstring& path, _In_ HANDLE hInput, _Inout_ wstring& result)
{
    wstring target;

    // Get the next target of a reparse point path.
    if (!TryGetNextTarget(path, hInput, target))
    {
        return false;
    }

    if (GetRootLength(target.c_str()) > 0)
    {
        // The next target is a rooted path, then return it as is.
        result.assign(target);
    }
    else
    {
        // The next target is a relative path, then resolve it first.
        if (!TryResolveRelativeTarget(path, target, result))
        {
            return false;
        }
    }

    return true;
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
/////////////////////////////////////////// Symlink traversal utilities /////////////////////////////////////////////////////////
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////


/// <summary>
/// Gets chains of the paths leading to and including the final path given the file name.
/// </summary>
static void DetourGetFinalPaths(_In_ const CanonicalizedPath& path, _In_ HANDLE hInput, _Inout_ vector<wstring>& finalPaths)
{
    finalPaths.push_back(path.GetPathString());

    wstring nextPath;

    if (!TryGetNextPath(path.GetPathString(), hInput, nextPath))
    {
        return;
    }

    DetourGetFinalPaths(CanonicalizedPath::Canonicalize(nextPath.c_str()), INVALID_HANDLE_VALUE, finalPaths);
}

/// <summary>
/// Checks if a path points to a directory.
/// </summary>
static bool IsPathToDirectory(_In_ LPCWSTR lpFileName, _In_ bool treatReparsePointAsFile)
{
    DWORD lastError = GetLastError();
    DWORD attributes = GetFileAttributesW(lpFileName);
    SetLastError(lastError);
    
    if (attributes == INVALID_FILE_ATTRIBUTES)
    {
        return false;
    }

    bool isDirectory = (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
    
    return (isDirectory && treatReparsePointAsFile) 
        ? ((attributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0)
        : isDirectory;
}

/// <summary>
/// Checks if a a handle is a handle of a directory.
/// </summary>
static bool TryCheckHandleOfDirectory(_In_ HANDLE hFile, _In_ bool treatReparsePointAsFile, _Out_ bool& isHandleOfDirectory)
{
    DWORD lastError = GetLastError();
    BY_HANDLE_FILE_INFORMATION fileInfo;
    BOOL res = GetFileInformationByHandle(hFile, &fileInfo);
    SetLastError(lastError);

    isHandleOfDirectory = res ? (fileInfo.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0 : false;

    if (isHandleOfDirectory && treatReparsePointAsFile)
    {
        isHandleOfDirectory = (fileInfo.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0;
    }

    return res ? true : false;
}


/// <summary>
/// Checks if a handle or a path points to a directory.
/// </summary>
/// <remarks>
/// This function first tries to get attributes via the given handle, and, if failed (e.g., the handle has
/// missing permisisons or is <code>INVALID_HANDLE_VALUE</code>), the function calls <code>GetFileAttributes</code> on the path.
/// </remarks>
static bool IsHandleOrPathToDirectory(_In_ HANDLE hFile, _In_ LPCWSTR lpFileName, bool treatReparsePointAsFile) 
{
    bool isHandleOfDirectory;

    return hFile == INVALID_HANDLE_VALUE || !TryCheckHandleOfDirectory(hFile, treatReparsePointAsFile, isHandleOfDirectory)
        ? IsPathToDirectory(lpFileName, treatReparsePointAsFile)
        : isHandleOfDirectory;
}

/// <summary>
/// Enforces allowed access for a particular path that leads to the target of a reparse point.
/// </summary>
static bool EnforceReparsePointAccess(
    const wstring& reparsePointPath,
    const DWORD dwDesiredAccess,
    const DWORD dwShareMode,
    const DWORD dwCreationDisposition,
    const DWORD dwFlagsAndAttributes,
    NTSTATUS* pNtStatus = nullptr,
    const bool enforceAccess = true,
    const bool isCreateDirectory = false)
{
    DWORD lastError = GetLastError();
    wstring fullPath(reparsePointPath);

    // We start with allow / ignore (no access requested) and then restrict based on read / write (maybe both, maybe neither!)
    AccessCheckResult accessCheck(RequestedAccess::None, ResultAction::Allow, ReportLevel::Ignore);

    FileOperationContext opContext(
        L"ReparsePointTarget",
        dwDesiredAccess,
        dwShareMode,
        dwCreationDisposition,
        dwFlagsAndAttributes,
        fullPath.c_str());

    bool ret = true;
    PolicyResult policyResult;

    if (!policyResult.Initialize(fullPath.c_str()))
    {
        policyResult.ReportIndeterminatePolicyAndSetLastError(opContext);
        SetLastError(lastError);
        return false;
    }

    // Enforce the access only we are not doing directory probing/enumeration.
    if (enforceAccess)
    {
        if (WantsWriteAccess(dwDesiredAccess))
        {
            if (isCreateDirectory)
            {
                accessCheck = policyResult.CheckCreateDirectoryAccess();
            }
            else
            {
                accessCheck = policyResult.CheckWriteAccess();
            }
        }

        if (WantsReadAccess(dwDesiredAccess))
        {
            FileReadContext readContext;
            WIN32_FIND_DATA findData;

            HANDLE findDataHandle = FindFirstFileW(fullPath.c_str(), &findData);

            if (findDataHandle != INVALID_HANDLE_VALUE)
            {
                readContext.FileExistence = FileExistence::Existent;
                FindClose(findDataHandle);
            }

            // Note that 'handle' is allowed invalid for this check. Some tools poke at directories without
            // FILE_FLAG_BACKUP_SEMANTICS and so get INVALID_HANDLE_VALUE / ERROR_ACCESS_DENIED. In that kind of
            // case we have a fallback to re-probe. See function remarks.
            // We skip this to avoid the fallback probe if we don't believe the path exists, since increasing failed-probe volume is dangerous for perf.
            readContext.OpenedDirectory = 
                (readContext.FileExistence == FileExistence::Existent) 
                && IsHandleOrPathToDirectory(INVALID_HANDLE_VALUE, fullPath.c_str(), false);

            accessCheck = AccessCheckResult::Combine(accessCheck, policyResult.CheckReadAccess(RequestedReadAccess::Read, readContext));
        }

        if (accessCheck.ShouldDenyAccess())
        {
            lastError = accessCheck.DenialError();

            if (pNtStatus != nullptr)
            {
                *pNtStatus = accessCheck.DenialNtStatus();
            }

            ret = false;
        }
    }

    // Report access to target.
    // If access to target were not reported, then we could have under-build. Suppose that the symlink and the target
    // are under a sealed directory, then BuildXL relies on observations (reports from Detours) to discover dynamic inputs.
    // If a pip launches a tool, and the tool accesses the target via the symlink only, and access to target were not reported, BuildXL would
    // discover the symlink as the only dynamic input. Thus, if the target is modified, BuildXL does not rebuild the corresponding pip.
    ReportIfNeeded(accessCheck, opContext, policyResult, lastError);
    SetLastError(lastError);

    return ret;
}

/// <summary>
/// Enforces allowed accesses for all paths leading to and including the target of a reparse point.
/// </summary>
/// <remarks>
/// This function calls <code>DetourGetFinalPaths</code> to get the sequence of paths leading to and including the target of a reparse point.
/// Having the sequence, this function calls <code>EnforceReparsePointAccess</code> on each path to check that access to that path is allowed.
/// </remarks>
static bool EnforceChainOfReparsePointAccesses(
    const CanonicalizedPath& path,
    HANDLE reparsePointHandle,
    const DWORD dwDesiredAccess,
    const DWORD dwShareMode,
    const DWORD dwCreationDisposition,
    const DWORD dwFlagsAndAttributes,
    const bool isNtCreate,
    NTSTATUS* pNtStatus = nullptr,
    const bool enforceAccess = true,
    const bool isCreateDirectory = false)
{
    if (IgnoreReparsePoints() || (isNtCreate && !MonitorNtCreateFile()))
    {
        return true;
    }

    vector<wstring> fullPaths;
    DetourGetFinalPaths(path, reparsePointHandle, fullPaths);

    bool success = true;

    for (vector<wstring>::iterator it = fullPaths.begin(); it != fullPaths.end(); ++it)
    {
        if (!EnforceReparsePointAccess(*it, dwDesiredAccess, dwShareMode, dwCreationDisposition, dwFlagsAndAttributes, pNtStatus, enforceAccess, isCreateDirectory))
        {
            success = false;
        }
    }

    return success;
}

/// <summary>
/// Enforces allowed accesses for all paths leading to and including the target of a reparse point for non CreateFile-like functions.
/// </summary>
static bool EnforceChainOfReparsePointAccessesForNonCreateFile(
    const FileOperationContext& fileOperationContext,
    const bool enforceAccess = true,
    const bool isCreateDirectory = false)
{
    if (!IgnoreNonCreateFileReparsePoints() && !IgnoreReparsePoints())
    {
        CanonicalizedPath canonicalPath = CanonicalizedPath::Canonicalize(fileOperationContext.NoncanonicalPath);
 
        if (IsReparsePoint(canonicalPath.GetPathString()))
        {
            bool accessResult = EnforceChainOfReparsePointAccesses(
                canonicalPath,
                INVALID_HANDLE_VALUE,
                fileOperationContext.DesiredAccess,
                fileOperationContext.ShareMode,
                fileOperationContext.CreationDisposition,
                fileOperationContext.FlagsAndAttributes,
                false,
                nullptr,
                enforceAccess,
                isCreateDirectory);

            if (!accessResult)
            {
                return false;
            }
        }
    }

    return true;
}

/// <summary>
/// Validates move directory by validating proper deletion for all source files and proper creation for all target files.
/// </summary>
static bool ValidateMoveDirectory(
    _In_      LPCWSTR                  sourceContext,
    _In_      LPCWSTR                  destinationContext,
    _In_      LPCWSTR                  lpExistingFileName,
    _In_opt_  LPCWSTR                  lpNewFileName,
    _Out_     vector<ReportData>&      filesAndDirectoriesToReport)
{
    DWORD error = GetLastError();

    vector<std::pair<wstring, DWORD>> filesAndDirectories;

    if (!EnumerateDirectory(lpExistingFileName, L"*", true, true, filesAndDirectories))
    {
        return false;
    }

    wstring sourceDirectory(lpExistingFileName);

    if (sourceDirectory.back() != L'\\')
    {
        sourceDirectory.push_back(L'\\');
    }

    wstring targetDirectory;

    if (lpNewFileName != NULL)
    {
        targetDirectory.assign(lpNewFileName);

        if (targetDirectory.back() != L'\\') 
        {
            targetDirectory.push_back(L'\\');
        }
    }

    for (vector<std::pair<wstring, DWORD>>::const_iterator it = filesAndDirectories.cbegin(); it != filesAndDirectories.cend(); ++it)
    {
        const std::pair<wstring, DWORD>& elem = *it;
        wstring file = elem.first;
        const DWORD& fileAttributes = elem.second;

        // Validate deletion of source.

        FileOperationContext sourceOpContext = FileOperationContext(
            sourceContext,
            DELETE,
            0,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL,
            file.c_str());

        PolicyResult sourcePolicyResult;
        if (!sourcePolicyResult.Initialize(file.c_str()))
        {
            sourcePolicyResult.ReportIndeterminatePolicyAndSetLastError(sourceOpContext);
            return false;
        }

        AccessCheckResult sourceAccessCheck = sourcePolicyResult.CheckWriteAccess();

        if (sourceAccessCheck.ShouldDenyAccess())
        {
            DWORD denyError = sourceAccessCheck.DenialError();
            ReportIfNeeded(sourceAccessCheck, sourceOpContext, sourcePolicyResult, denyError);
            sourceAccessCheck.SetLastErrorToDenialError();
            return false;
        }

        filesAndDirectoriesToReport.push_back(ReportData(sourceAccessCheck, sourceOpContext, sourcePolicyResult));

        // Validate creation of target.

        if (lpNewFileName != NULL)
        {
            file.replace(0, sourceDirectory.length(), targetDirectory);

            FileOperationContext destinationOpContext = FileOperationContext(
                destinationContext,
                GENERIC_WRITE,
                0,
                CREATE_ALWAYS,
                FILE_ATTRIBUTE_NORMAL,
                file.c_str());

            PolicyResult destPolicyResult;

            if (!destPolicyResult.Initialize(file.c_str()))
            {
                destPolicyResult.ReportIndeterminatePolicyAndSetLastError(destinationOpContext);
                return false;
            }

            AccessCheckResult destAccessCheck = (fileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0
                ? destPolicyResult.CheckCreateDirectoryAccess()
                : destPolicyResult.CheckWriteAccess();

            if (destAccessCheck.ShouldDenyAccess())
            {
                // We report the destination access here since we are returning early. Otherwise it is deferred until post-read.
                DWORD denyError = destAccessCheck.DenialError();
                ReportIfNeeded(destAccessCheck, destinationOpContext, destPolicyResult, denyError);
                destAccessCheck.SetLastErrorToDenialError();
                return false;
            }

            filesAndDirectoriesToReport.push_back(ReportData(destAccessCheck, destinationOpContext, destPolicyResult));
        }
    }

    SetLastError(error);

    return true;
}

typedef enum _FILE_INFORMATION_CLASS_EXTRA {
    FileFullDirectoryInformation = 2,
    FileBothDirectoryInformation,
    FileBasicInformation,
    FileStandardInformation,
    FileInternalInformation,
    FileEaInformation,
    FileAccessInformation,
    FileNameInformation,
    FileRenameInformation,
    FileLinkInformation,
    FileNamesInformation,
    FileDispositionInformation,
    FilePositionInformation,
    FileFullEaInformation,
    FileModeInformation,
    FileAlignmentInformation,
    FileAllInformation,
    FileAllocationInformation,
    FileEndOfFileInformation,
    FileAlternateNameInformation,
    FileStreamInformation,
    FilePipeInformation,
    FilePipeLocalInformation,
    FilePipeRemoteInformation,
    FileMailslotQueryInformation,
    FileMailslotSetInformation,
    FileCompressionInformation,
    FileObjectIdInformation,
    FileCompletionInformation,
    FileMoveClusterInformation,
    FileQuotaInformation,
    FileReparsePointInformation,
    FileNetworkOpenInformation,
    FileAttributeTagInformation,
    FileTrackingInformation,
    FileIdBothDirectoryInformation,
    FileIdFullDirectoryInformation,
    FileValidDataLengthInformation,
    FileShortNameInformation,
    FileIoCompletionNotificationInformation,
    FileIoStatusBlockRangeInformation,
    FileIoPriorityHintInformation,
    FileSfioReserveInformation,
    FileSfioVolumeInformation,
    FileHardLinkInformation,
    FileProcessIdsUsingFileInformation,
    FileNormalizedNameInformation,
    FileNetworkPhysicalNameInformation,
    FileIdGlobalTxDirectoryInformation,
    FileIsRemoteDeviceInformation,
    FileUnusedInformation,
    FileNumaNodeInformation,
    FileStandardLinkInformation,
    FileRemoteProtocolInformation,
    FileRenameInformationBypassAccessCheck,
    FileLinkInformationBypassAccessCheck,
    FileVolumeNameInformation,
    FileIdInformation,
    FileIdExtdDirectoryInformation,
    FileReplaceCompletionInformation,
    FileHardLinkFullIdInformation,
    FileIdExtdBothDirectoryInformation,
    FileDispositionInformationEx,
    FileRenameInformationEx,
    FileRenameInformationExBypassAccessCheck,
    FileDesiredStorageClassInformation,
    FileStatInformation,
    FileMemoryPartitionInformation,
    FileStatLxInformation,
    FileCaseSensitiveInformation,
    FileLinkInformationEx,
    FileLinkInformationExBypassAccessCheck,
    FileStorageReserveIdInformation,
    FileCaseSensitiveInformationForceAccessCheck,
    FileMaximumInformation
} FILE_INFORMATION_CLASS_EXTRA, *PFILE_INFORMATION_CLASS_EXTRA;

typedef struct _FILE_RENAME_INFORMATION {
    BOOLEAN ReplaceIfExists;
    HANDLE  RootDirectory;
    ULONG   FileNameLength;
    WCHAR   FileName[1];
} FILE_RENAME_INFORMATION, *PFILE_RENAME_INFORMATION;

typedef struct _FILE_LINK_INFORMATION {
    BOOLEAN ReplaceIfExists;
    HANDLE  RootDirectory;
    ULONG   FileNameLength;
    WCHAR   FileName[1];
} FILE_LINK_INFORMATION, *PFILE_LINK_INFORMATION;

// This struct is very similar to _FILE_LINK_INFORMATION. If ULONG is 4 bytes long,
// these two structs even have the same layout:
//   a) BOOLEAN is 1 byte long, but in this struct a compiler, by default, will pad it to 4 bytes
//   b) union is as long as it's biggest member (i.e., ULONG in this case)
// However, there is no guarantee that ULONG is 4 bytes long (in some scenarios, it can be 8 bytes long). 
// This structure has been introduced, so we wouldn't depend on the ULONG's length when casting/dereferencing   PVOID.
typedef struct _FILE_LINK_INFORMATION_EX {
    union {
        BOOLEAN ReplaceIfExists;
        ULONG Flags;
    };
    HANDLE  RootDirectory;
    ULONG   FileNameLength;
    WCHAR   FileName[1];
} FILE_LINK_INFORMATION_EX, *PFILE_LINK_INFORMATION_EX;

typedef struct _FILE_NAME_INFORMATION {
    ULONG FileNameLength;
    WCHAR FileName[1];
} FILE_NAME_INFORMATION, *PFILE_NAME_INFORMATION;

typedef struct _FILE_DISPOSITION_INFORMATION {
    BOOLEAN DeleteFile;
} FILE_DISPOSITION_INFORMATION, *PFILE_DISPOSITION_INFORMATION;

typedef struct _FILE_MODE_INFORMATION {
    ULONG Mode;
} FILE_MODE_INFORMATION, *PFILE_MODE_INFORMATION;

static bool TryGetFileNameFromFileInformation(
    _In_  PWCHAR   fileName,
    _In_  ULONG    fileNameLength,
    _In_  HANDLE   rootDirectory,
    _Out_ wstring& result)
{
    result.assign(fileName, (size_t)(fileNameLength / sizeof(WCHAR)));

    DWORD lastError = GetLastError();

    // See https://msdn.microsoft.com/en-us/library/windows/hardware/ff540344(v=vs.85).aspx
    // See https://msdn.microsoft.com/en-us/library/windows/hardware/ff540324(v=vs.85).aspx
    // RootDirectory:
    //      If the file is not being moved to a different directory, or if the FileName member contains the full pathname, this member is NULL.
    //      Otherwise, it is a handle for the root directory under which the file will reside after it is renamed.
    // FileName:
    //      The first character of a wide - character string containing the new name for the file. This is followed in memory by the remainder of the string.
    //      If the RootDirectory member is NULL, and the file is being moved/linked to a different directory, this member specifies the full pathname 
    //      to be assigned to the file. Otherwise, it specifies only the file name or a relative pathname.
    if (rootDirectory != nullptr || rootDirectory != NULL)
    {
        wstring dirPath;

        if (DetourGetFinalPathByHandle(rootDirectory, dirPath) != ERROR_SUCCESS)
        {
            Dbg(L"TryGetFileNameFromFileInformation: DetourGetFinalPathByHandle: %d", GetLastError());
            SetLastError(lastError);
            return false;
        }

        CanonicalizedPath dirPathCan = CanonicalizedPath::Canonicalize(dirPath.c_str());
        CanonicalizedPath dirPathExtended = dirPathCan.Extend(result.c_str());

        result.assign(dirPathExtended.GetPathString());
    }

    SetLastError(lastError);
    return true;
}

NTSTATUS HandleFileRenameInformation(
    _In_  HANDLE                 FileHandle,
    _Out_ PIO_STATUS_BLOCK       IoStatusBlock,
    _In_  PVOID                  FileInformation,
    _In_  ULONG                  Length,
    _In_  FILE_INFORMATION_CLASS FileInformationClass)
{
    assert((FILE_INFORMATION_CLASS_EXTRA)FileInformationClass == FILE_INFORMATION_CLASS_EXTRA::FileRenameInformation);

    DetouredScope scope;
    if (scope.Detoured_IsDisabled())
    {
        return Real_ZwSetInformationFile(
            FileHandle,
            IoStatusBlock,
            FileInformation,
            Length,
            FileInformationClass);
    }

    DWORD lastError = GetLastError();
    wstring sourcePath;

    DWORD getFinalPathByHandle = DetourGetFinalPathByHandle(FileHandle, sourcePath);
    if ((getFinalPathByHandle != ERROR_SUCCESS) || IsSpecialDeviceName(sourcePath.c_str()) || IsNullOrEmptyW(sourcePath.c_str()))
    {
        if (getFinalPathByHandle != ERROR_SUCCESS)
        {
            Dbg(L"HandleFileRenameInformation: DetourGetFinalPathByHandle: %d", getFinalPathByHandle);
        }

        SetLastError(lastError);

        return Real_ZwSetInformationFile(
            FileHandle,
            IoStatusBlock,
            FileInformation,
            Length,
            FileInformationClass);
    }

    PFILE_RENAME_INFORMATION pRenameInfo = (PFILE_RENAME_INFORMATION)FileInformation;

    wstring targetPath;

    if (!TryGetFileNameFromFileInformation(
            pRenameInfo->FileName, 
            pRenameInfo->FileNameLength, 
            pRenameInfo->RootDirectory,
            targetPath)
        || targetPath.empty())
    {
        SetLastError(lastError);

        return Real_ZwSetInformationFile(
            FileHandle,
            IoStatusBlock,
            FileInformation,
            Length,
            FileInformationClass);
    }

    FileOperationContext sourceOpContext = FileOperationContext(
        L"ZwSetRenameInformationFile_Source",
        DELETE,
        0,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        sourcePath.c_str());

    PolicyResult sourcePolicyResult;

    if (!sourcePolicyResult.Initialize(sourcePath.c_str()))
    {
        sourcePolicyResult.ReportIndeterminatePolicyAndSetLastError(sourceOpContext);
        return DETOURS_STATUS_ACCESS_DENIED;
    }

    FileOperationContext destinationOpContext = FileOperationContext(
        L"ZwSetRenameInformationFile_Dest",
        GENERIC_WRITE,
        0,
        CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL,
        targetPath.c_str());

    PolicyResult destPolicyResult;

    if (!destPolicyResult.Initialize(targetPath.c_str()))
    {
        destPolicyResult.ReportIndeterminatePolicyAndSetLastError(destinationOpContext);
        return DETOURS_STATUS_ACCESS_DENIED;
    }

    // Writes are destructive. Before doing a move we ensure that write access is definitely allowed to the source (delete) and destination (write).
    AccessCheckResult sourceAccessCheck = sourcePolicyResult.CheckWriteAccess();

    if (sourceAccessCheck.ShouldDenyAccess()) 
    {
        ReportIfNeeded(sourceAccessCheck, sourceOpContext, sourcePolicyResult, sourceAccessCheck.DenialError());
        sourceAccessCheck.SetLastErrorToDenialError();
        return sourceAccessCheck.DenialNtStatus();
    }

    AccessCheckResult destAccessCheck = destPolicyResult.CheckWriteAccess();

    if (destAccessCheck.ShouldDenyAccess())
    {
        ReportIfNeeded(destAccessCheck, destinationOpContext, destPolicyResult, destAccessCheck.DenialError());
        destAccessCheck.SetLastErrorToDenialError();
        return destAccessCheck.DenialNtStatus();
    }

    bool isHandleOfDirectory;
    bool renameDirectory = false;
    vector<ReportData> filesAndDirectoriesToReport;

    if (TryCheckHandleOfDirectory(FileHandle, true, isHandleOfDirectory) && isHandleOfDirectory)
    {
        renameDirectory = true;

        if (!ValidateMoveDirectory(
                L"ZwSetRenameInformationFile_Source", 
                L"ZwSetRenameInformationFile_Dest",
                sourcePath.c_str(), 
                targetPath.c_str(), 
                filesAndDirectoriesToReport))
        {
            return FALSE;
        }
    }

    SetLastError(lastError);

    NTSTATUS result = Real_ZwSetInformationFile(
        FileHandle,
        IoStatusBlock,
        FileInformation,
        Length,
        FileInformationClass);

    if (!NT_SUCCESS(result))
    {
        lastError = GetLastError();
    }

    DWORD ntError = RtlNtStatusToDosError(result);

    ReportIfNeeded(sourceAccessCheck, sourceOpContext, sourcePolicyResult, ntError);
    ReportIfNeeded(destAccessCheck, destinationOpContext, destPolicyResult, ntError);

    if (renameDirectory)
    {
        for (vector<ReportData>::const_iterator it = filesAndDirectoriesToReport.cbegin(); it != filesAndDirectoriesToReport.cend(); ++it)
        {
            ReportIfNeeded(it->GetAccessCheckResult(), it->GetFileOperationContext(), it->GetPolicyResult(), ntError);
        }
    }

    SetLastError(lastError);

    return result;
}

NTSTATUS HandleFileLinkInformation(
    _In_  HANDLE                 FileHandle,
    _Out_ PIO_STATUS_BLOCK       IoStatusBlock,
    _In_  PVOID                  FileInformation,
    _In_  ULONG                  Length,
    _In_  FILE_INFORMATION_CLASS FileInformationClass,
    _In_  BOOL                   IsExtendedFileInformation)
{
    assert((!IsExtendedFileInformation && (FILE_INFORMATION_CLASS_EXTRA)FileInformationClass == FILE_INFORMATION_CLASS_EXTRA::FileLinkInformation)
         || (IsExtendedFileInformation && (FILE_INFORMATION_CLASS_EXTRA)FileInformationClass == FILE_INFORMATION_CLASS_EXTRA::FileLinkInformationEx));

    DetouredScope scope;
    if (scope.Detoured_IsDisabled())
    {
        return Real_ZwSetInformationFile(
            FileHandle,
            IoStatusBlock,
            FileInformation,
            Length,
            FileInformationClass);
    }

    DWORD lastError = GetLastError();

    PWCHAR fileName;
    ULONG fileNameLength;
    HANDLE rootDirectory; 
    if (!IsExtendedFileInformation) {
        PFILE_LINK_INFORMATION pLinkInfo = (PFILE_LINK_INFORMATION)FileInformation;
        fileName = pLinkInfo->FileName;
        fileNameLength = pLinkInfo->FileNameLength;
        rootDirectory = pLinkInfo->RootDirectory;
    }
    else {
        PFILE_LINK_INFORMATION_EX pLinkInfoEx = (PFILE_LINK_INFORMATION_EX)FileInformation;
        fileName = pLinkInfoEx->FileName;
        fileNameLength = pLinkInfoEx->FileNameLength;
        rootDirectory = pLinkInfoEx->RootDirectory;
    }

    wstring targetPath;   

    if (!TryGetFileNameFromFileInformation(
        fileName,
        fileNameLength,
        rootDirectory,
        targetPath)
        || targetPath.empty())
    {
        SetLastError(lastError);

        return Real_ZwSetInformationFile(
            FileHandle,
            IoStatusBlock,
            FileInformation,
            Length,
            FileInformationClass);
    }


    FileOperationContext targetOpContext = FileOperationContext(
        L"ZwSetLinkInformationFile",
        DELETE,
        0,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        targetPath.c_str());

    PolicyResult targetPolicyResult;

    if (!targetPolicyResult.Initialize(targetPath.c_str()))
    {
        targetPolicyResult.ReportIndeterminatePolicyAndSetLastError(targetOpContext);
        return FALSE;
    }

    AccessCheckResult targetAccessCheck = targetPolicyResult.CheckWriteAccess();

    if (targetAccessCheck.ShouldDenyAccess())
    {
        ReportIfNeeded(targetAccessCheck, targetOpContext, targetPolicyResult, targetAccessCheck.DenialError());
        targetAccessCheck.SetLastErrorToDenialError();
        return targetAccessCheck.DenialNtStatus();
    }

    SetLastError(lastError);

    NTSTATUS result = Real_ZwSetInformationFile(
        FileHandle,
        IoStatusBlock,
        FileInformation,
        Length,
        FileInformationClass);

    if (!NT_SUCCESS(result))
    {
        lastError = GetLastError();
    }

    ReportIfNeeded(targetAccessCheck, targetOpContext, targetPolicyResult, RtlNtStatusToDosError(result));

    SetLastError(lastError);

    return result;
}

NTSTATUS HandleFileDispositionInformation(
    _In_  HANDLE                 FileHandle,
    _Out_ PIO_STATUS_BLOCK       IoStatusBlock,
    _In_  PVOID                  FileInformation,
    _In_  ULONG                  Length,
    _In_  FILE_INFORMATION_CLASS FileInformationClass)
{
    assert((FILE_INFORMATION_CLASS_EXTRA)FileInformationClass == FILE_INFORMATION_CLASS_EXTRA::FileDispositionInformation);

    PFILE_DISPOSITION_INFORMATION pDispositionInfo = (PFILE_DISPOSITION_INFORMATION)FileInformation;

    DetouredScope scope;
    if (scope.Detoured_IsDisabled() || !pDispositionInfo->DeleteFile)
    {
        return Real_ZwSetInformationFile(
            FileHandle,
            IoStatusBlock,
            FileInformation,
            Length,
            FileInformationClass);
    }

    DWORD lastError = GetLastError();
    wstring sourcePath;

    DWORD getFinalPathByHandle = DetourGetFinalPathByHandle(FileHandle, sourcePath);
    if ((getFinalPathByHandle != ERROR_SUCCESS) || IsSpecialDeviceName(sourcePath.c_str()) || IsNullOrEmptyW(sourcePath.c_str()))
    {
        if (getFinalPathByHandle != ERROR_SUCCESS)
        {
            Dbg(L"HandleFileDispositionInformation: DetourGetFinalPathByHandle: %d", getFinalPathByHandle);
        }

        SetLastError(lastError);

        return Real_ZwSetInformationFile(
            FileHandle,
            IoStatusBlock,
            FileInformation,
            Length,
            FileInformationClass);
    }

    FileOperationContext sourceOpContext = FileOperationContext(
        L"ZwSetDispositionInformationFile",
        DELETE,
        0,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        sourcePath.c_str());

    PolicyResult sourcePolicyResult;

    if (!sourcePolicyResult.Initialize(sourcePath.c_str()))
    {
        sourcePolicyResult.ReportIndeterminatePolicyAndSetLastError(sourceOpContext);
        return DETOURS_STATUS_ACCESS_DENIED;
    }

    AccessCheckResult sourceAccessCheck = sourcePolicyResult.CheckWriteAccess();

    if (sourceAccessCheck.ShouldDenyAccess()) 
    {
        ReportIfNeeded(sourceAccessCheck, sourceOpContext, sourcePolicyResult, sourceAccessCheck.DenialError());
        sourceAccessCheck.SetLastErrorToDenialError();
        return sourceAccessCheck.DenialNtStatus();
    }

    SetLastError(lastError);

    NTSTATUS result = Real_ZwSetInformationFile(
        FileHandle,
        IoStatusBlock,
        FileInformation,
        Length,
        FileInformationClass);

    if (!NT_SUCCESS(result))
    {
        lastError = GetLastError();
    }
    
    ReportIfNeeded(sourceAccessCheck, sourceOpContext, sourcePolicyResult, RtlNtStatusToDosError(result));
    
    SetLastError(lastError);

    return result;
}

NTSTATUS HandleFileModeInformation(
    _In_  HANDLE                 FileHandle,
    _Out_ PIO_STATUS_BLOCK       IoStatusBlock,
    _In_  PVOID                  FileInformation,
    _In_  ULONG                  Length,
    _In_  FILE_INFORMATION_CLASS FileInformationClass)
{
    assert((FILE_INFORMATION_CLASS_EXTRA)FileInformationClass == FILE_INFORMATION_CLASS_EXTRA::FileModeInformation);

    PFILE_MODE_INFORMATION pModeInfo = (PFILE_MODE_INFORMATION)FileInformation;

    DetouredScope scope;
    if (scope.Detoured_IsDisabled() || ((pModeInfo->Mode & FILE_DELETE_ON_CLOSE) == 0))
    {
        return Real_ZwSetInformationFile(
            FileHandle,
            IoStatusBlock,
            FileInformation,
            Length,
            FileInformationClass);
    }

    DWORD lastError = GetLastError();
    wstring sourcePath;

    DWORD getFinalPathByHandle = DetourGetFinalPathByHandle(FileHandle, sourcePath);
    if ((getFinalPathByHandle != ERROR_SUCCESS) || IsSpecialDeviceName(sourcePath.c_str()) || IsNullOrEmptyW(sourcePath.c_str()))
    {
        if (getFinalPathByHandle != ERROR_SUCCESS)
        {
            Dbg(L"HandleFileModeInformation: DetourGetFinalPathByHandle: %d", getFinalPathByHandle);
        }

        SetLastError(lastError);

        return Real_ZwSetInformationFile(
            FileHandle,
            IoStatusBlock,
            FileInformation,
            Length,
            FileInformationClass);
    }

    FileOperationContext sourceOpContext = FileOperationContext(
        L"ZwSetModeInformationFile",
        DELETE,
        0,
        OPEN_EXISTING,
        FILE_FLAG_DELETE_ON_CLOSE,
        sourcePath.c_str());

    PolicyResult sourcePolicyResult;

    if (!sourcePolicyResult.Initialize(sourcePath.c_str()))
    {
        sourcePolicyResult.ReportIndeterminatePolicyAndSetLastError(sourceOpContext);
        return DETOURS_STATUS_ACCESS_DENIED;
    }

    AccessCheckResult sourceAccessCheck = sourcePolicyResult.CheckWriteAccess();

    if (sourceAccessCheck.ShouldDenyAccess()) 
    {
        ReportIfNeeded(sourceAccessCheck, sourceOpContext, sourcePolicyResult, sourceAccessCheck.DenialError());
        sourceAccessCheck.SetLastErrorToDenialError();
        return sourceAccessCheck.DenialNtStatus();
    }

    SetLastError(lastError);

    NTSTATUS result = Real_ZwSetInformationFile(
        FileHandle,
        IoStatusBlock,
        FileInformation,
        Length,
        FileInformationClass);

    if (!NT_SUCCESS(result))
    {
        lastError = GetLastError();
    }
    
    ReportIfNeeded(sourceAccessCheck, sourceOpContext, sourcePolicyResult, RtlNtStatusToDosError(result));

    SetLastError(lastError);

    return result;
}

NTSTATUS HandleFileNameInformation(
    _In_  HANDLE                 FileHandle,
    _Out_ PIO_STATUS_BLOCK       IoStatusBlock,
    _In_  PVOID                  FileInformation,
    _In_  ULONG                  Length,
    _In_  FILE_INFORMATION_CLASS FileInformationClass)
{
    assert((FILE_INFORMATION_CLASS_EXTRA)FileInformationClass == FILE_INFORMATION_CLASS_EXTRA::FileNameInformation);

    DetouredScope scope;
    if (scope.Detoured_IsDisabled())
    {
        return Real_ZwSetInformationFile(
            FileHandle,
            IoStatusBlock,
            FileInformation,
            Length,
            FileInformationClass);
    }

    DWORD lastError = GetLastError();
    wstring sourcePath;

    DWORD getFinalPathByHandle = DetourGetFinalPathByHandle(FileHandle, sourcePath);
    if ((getFinalPathByHandle != ERROR_SUCCESS) || IsSpecialDeviceName(sourcePath.c_str()) || IsNullOrEmptyW(sourcePath.c_str()))
    {
        if (getFinalPathByHandle != ERROR_SUCCESS)
        {
            Dbg(L"HandleFileNameInformation: DetourGetFinalPathByHandle: %d", getFinalPathByHandle);
        }

        SetLastError(lastError);

        return Real_ZwSetInformationFile(
            FileHandle,
            IoStatusBlock,
            FileInformation,
            Length,
            FileInformationClass);
    }

    PFILE_NAME_INFORMATION pNameInfo = (PFILE_NAME_INFORMATION)FileInformation;

    wstring targetPath;

    if (!TryGetFileNameFromFileInformation(
        pNameInfo->FileName,
        pNameInfo->FileNameLength,
        nullptr,
        targetPath)
        || targetPath.empty())
    {
        SetLastError(lastError);

        return Real_ZwSetInformationFile(
            FileHandle,
            IoStatusBlock,
            FileInformation,
            Length,
            FileInformationClass);
    }

    FileOperationContext sourceOpContext = FileOperationContext(
        L"ZwSetFileNameInformationFile_Source",
        DELETE,
        0,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        sourcePath.c_str());

    PolicyResult sourcePolicyResult;

    if (!sourcePolicyResult.Initialize(sourcePath.c_str()))
    {
        sourcePolicyResult.ReportIndeterminatePolicyAndSetLastError(sourceOpContext);
        return DETOURS_STATUS_ACCESS_DENIED;
    }

    FileOperationContext destinationOpContext = FileOperationContext(
        L"ZwSetFileNameInformationFile_Dest",
        GENERIC_WRITE,
        0,
        CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL,
        targetPath.c_str());

    PolicyResult destPolicyResult;

    if (!destPolicyResult.Initialize(targetPath.c_str()))
    {
        destPolicyResult.ReportIndeterminatePolicyAndSetLastError(destinationOpContext);
        return DETOURS_STATUS_ACCESS_DENIED;
    }

    // Writes are destructive. Before doing a move we ensure that write access is definitely allowed to the source (delete) and destination (write).
    AccessCheckResult sourceAccessCheck = sourcePolicyResult.CheckWriteAccess();

    if (sourceAccessCheck.ShouldDenyAccess()) 
    {
        ReportIfNeeded(sourceAccessCheck, sourceOpContext, sourcePolicyResult, sourceAccessCheck.DenialError());
        sourceAccessCheck.SetLastErrorToDenialError();
        return sourceAccessCheck.DenialNtStatus();
    }

    AccessCheckResult destAccessCheck = destPolicyResult.CheckWriteAccess();

    if (destAccessCheck.ShouldDenyAccess())
    {
        ReportIfNeeded(destAccessCheck, destinationOpContext, destPolicyResult, destAccessCheck.DenialError());
        destAccessCheck.SetLastErrorToDenialError();
        return destAccessCheck.DenialNtStatus();
    }

    bool isHandleOfDirectory;
    bool renameDirectory = false;
    vector<ReportData> filesAndDirectoriesToReport;

    if (TryCheckHandleOfDirectory(FileHandle, true, isHandleOfDirectory) && isHandleOfDirectory)
    {
        renameDirectory = true;

        if (!ValidateMoveDirectory(
                L"ZwSetFileNameInformationFile_Source", 
                L"ZwSetFileNameInformationFile_Dest",
                sourcePath.c_str(), 
                targetPath.c_str(), 
                filesAndDirectoriesToReport))
        {
            return FALSE;
        }
    }

    SetLastError(lastError);

    NTSTATUS result = Real_ZwSetInformationFile(
        FileHandle,
        IoStatusBlock,
        FileInformation,
        Length,
        FileInformationClass);

    if (!NT_SUCCESS(result))
    {
        lastError = GetLastError();
    }

    DWORD ntError = RtlNtStatusToDosError(result);
    
    ReportIfNeeded(sourceAccessCheck, sourceOpContext, sourcePolicyResult, ntError);
    ReportIfNeeded(destAccessCheck, destinationOpContext, destPolicyResult, ntError);

    if (renameDirectory)
    {
        for (vector<ReportData>::const_iterator it = filesAndDirectoriesToReport.cbegin(); it != filesAndDirectoriesToReport.cend(); ++it)
        {
            ReportIfNeeded(it->GetAccessCheckResult(), it->GetFileOperationContext(), it->GetPolicyResult(), ntError);
        }
    }

    SetLastError(lastError);

    return result;
}

IMPLEMENTED(Detoured_ZwSetInformationFile)
NTSTATUS NTAPI Detoured_ZwSetInformationFile(
    _In_  HANDLE                 FileHandle,
    _Out_ PIO_STATUS_BLOCK       IoStatusBlock,
    _In_  PVOID                  FileInformation,
    _In_  ULONG                  Length,
    _In_  FILE_INFORMATION_CLASS FileInformationClass)
{
    // if this is not an enabled case that we are covering, just call the Real_Function.
    FILE_INFORMATION_CLASS_EXTRA fileInformationClassExtra = (FILE_INFORMATION_CLASS_EXTRA)FileInformationClass;

    switch (fileInformationClassExtra)
    {
        case FILE_INFORMATION_CLASS_EXTRA::FileRenameInformation:
            if (!IgnoreZwRenameFileInformation()) 
            {
                return HandleFileRenameInformation(FileHandle, IoStatusBlock, FileInformation, Length, FileInformationClass);
            }
            break;
        case FILE_INFORMATION_CLASS_EXTRA::FileLinkInformation:
        case FILE_INFORMATION_CLASS_EXTRA::FileLinkInformationEx:
            if (!IgnoreZwOtherFileInformation())
            {
                return HandleFileLinkInformation(FileHandle, IoStatusBlock, FileInformation, Length, FileInformationClass, fileInformationClassExtra == FILE_INFORMATION_CLASS_EXTRA::FileLinkInformationEx);
            }
            break;
        case FILE_INFORMATION_CLASS_EXTRA::FileDispositionInformation:
            if (!IgnoreZwOtherFileInformation())
            {
                return HandleFileDispositionInformation(FileHandle, IoStatusBlock, FileInformation, Length, FileInformationClass);
            }
            break;
        case FILE_INFORMATION_CLASS_EXTRA::FileModeInformation:
            if (!IgnoreZwOtherFileInformation())
            {
                return HandleFileModeInformation(FileHandle, IoStatusBlock, FileInformation, Length, FileInformationClass);
            }
            break;
        case FILE_INFORMATION_CLASS_EXTRA::FileNameInformation:
            if (!IgnoreZwOtherFileInformation())
            {
                return HandleFileNameInformation(FileHandle, IoStatusBlock, FileInformation, Length, FileInformationClass);
            }
            break;
        default:
            break;
// Without the warning suppression below, some compilation flag can produce a warning because the cases aboe are not
// exhaustive with respect to the FILE_INFORMATION_CLASS_EXTRA enums.
#pragma warning(suppress: 4061)
    }

    return Real_ZwSetInformationFile(
        FileHandle,
        IoStatusBlock,
        FileInformation,
        Length,
        FileInformationClass);
}

IMPLEMENTED(Detoured_CreateProcessW)
BOOL WINAPI Detoured_CreateProcessW(
    _In_opt_    LPCWSTR               lpApplicationName,
    _Inout_opt_ LPWSTR                lpCommandLine,
    _In_opt_    LPSECURITY_ATTRIBUTES lpProcessAttributes,
    _In_opt_    LPSECURITY_ATTRIBUTES lpThreadAttributes,
    _In_        BOOL                  bInheritHandles,
    _In_        DWORD                 dwCreationFlags,
    _In_opt_    LPVOID                lpEnvironment,
    _In_opt_    LPCWSTR               lpCurrentDirectory,
    _In_        LPSTARTUPINFOW        lpStartupInfo,
    _Out_       LPPROCESS_INFORMATION lpProcessInformation)
{
    bool injectedShim = false;
    BOOL ret = MaybeInjectSubstituteProcessShim(
        lpApplicationName,
        lpCommandLine,
        lpProcessAttributes,
        lpThreadAttributes,
        bInheritHandles,
        dwCreationFlags,
        lpEnvironment,
        lpCurrentDirectory,
        lpStartupInfo,
        lpProcessInformation,
        injectedShim);
    if (injectedShim)
    {
        Dbg(L"Injected shim for lpCommandLine='%s', returning 0x%08X from CreateProcessW", lpCommandLine, ret);
        return ret;
    }

    if (!MonitorChildProcesses())
    {
        return Real_CreateProcessW(
            lpApplicationName,
            lpCommandLine,
            lpProcessAttributes,
            lpThreadAttributes,
            bInheritHandles,
            dwCreationFlags,
            lpEnvironment,
            lpCurrentDirectory,
            lpStartupInfo,
            lpProcessInformation);
    }

    bool retryCreateProcess = true;
    unsigned retryCount = 0;

    while (retryCreateProcess)
    {
        retryCreateProcess = false;
        // Make sure we pass the Real_CreateProcessW such that it calls into the prior entry point
        CreateDetouredProcessStatus status = InternalCreateDetouredProcess(
            lpApplicationName,
            lpCommandLine,
            lpProcessAttributes,
            lpThreadAttributes,
            bInheritHandles,
            dwCreationFlags,
            lpEnvironment,
            lpCurrentDirectory,
            lpStartupInfo,
            (HANDLE)0,
            g_pDetouredProcessInjector,
            lpProcessInformation,
            Real_CreateProcessW);

        if (status == CreateDetouredProcessStatus::Succeeded) 
        {
            return TRUE;
        }
        else if (status == CreateDetouredProcessStatus::ProcessCreationFailed) 
        {
            // Process creation failure is something normally visible to the caller. Preserve last error information.
            return FALSE;
        }
        else 
        {
            Dbg(L"Failure Detouring the process - Error: 0x%08X.", GetLastError());
            
            if (GetLastError() == ERROR_INVALID_FUNCTION &&
                retryCount < RETRY_DETOURING_PROCESS_COUNT)
            {
                Sleep(1000); // Wait a second and try again.
                retryCount++;
                Dbg(L"Retrying to start process %s for %d time.", lpCommandLine, retryCount);
                retryCreateProcess = true;
                SetLastError(ERROR_SUCCESS);
                continue;
            }

            // We've invented a failure other than process creation due to our detours; invent a consistent error
            // rather than leaking whatever error might be set due to our failed efforts.
            SetLastError(ERROR_ACCESS_DENIED);
            return FALSE;
        }
    }

    return TRUE;
}

IMPLEMENTED(Detoured_CreateProcessA)
BOOL WINAPI Detoured_CreateProcessA(
    _In_opt_    LPCSTR                lpApplicationName,
    _Inout_opt_ LPSTR                 lpCommandLine,
    _In_opt_    LPSECURITY_ATTRIBUTES lpProcessAttributes,
    _In_opt_    LPSECURITY_ATTRIBUTES lpThreadAttributes,
    _In_        BOOL                  bInheritHandles,
    _In_        DWORD                 dwCreationFlags,
    _In_opt_    LPVOID                lpEnvironment,
    _In_opt_    LPCSTR                lpCurrentDirectory,
    _In_        LPSTARTUPINFOA        lpStartupInfo,
    _Out_       LPPROCESS_INFORMATION lpProcessInformation)
{
    // Note that we only do Real_CreateProcessA
    // for the case of not doing child processes.
    // Otherwise this converts to CreateProcessW
    if (!MonitorChildProcesses())
    {
        return Real_CreateProcessA(
            lpApplicationName,
            lpCommandLine,
            lpProcessAttributes,
            lpThreadAttributes,
            bInheritHandles,
            dwCreationFlags,
            lpEnvironment,
            lpCurrentDirectory,
            lpStartupInfo,
            lpProcessInformation);
    }

    UnicodeConverter applicationName(lpApplicationName);
    UnicodeConverter commandLine(lpCommandLine);
    UnicodeConverter currentDirectory(lpCurrentDirectory);

    UnicodeConverter desktop(lpStartupInfo->lpDesktop);
    UnicodeConverter title(lpStartupInfo->lpTitle);

    STARTUPINFOW startupInfo;
    startupInfo.cb = sizeof(STARTUPINFOW);
    startupInfo.lpReserved = NULL;
    startupInfo.lpDesktop = desktop.GetMutableString();
    startupInfo.lpTitle = title.GetMutableString();
    startupInfo.dwX = lpStartupInfo->dwX;
    startupInfo.dwY = lpStartupInfo->dwY;
    startupInfo.dwXSize = lpStartupInfo->dwXSize;
    startupInfo.dwYSize = lpStartupInfo->dwYSize;
    startupInfo.dwXCountChars = lpStartupInfo->dwXCountChars;
    startupInfo.dwYCountChars = lpStartupInfo->dwYCountChars;
    startupInfo.dwFillAttribute = lpStartupInfo->dwFillAttribute;
    startupInfo.dwFlags = lpStartupInfo->dwFlags;
    startupInfo.wShowWindow = lpStartupInfo->wShowWindow;
    startupInfo.cbReserved2 = lpStartupInfo->cbReserved2;
    startupInfo.lpReserved2 = lpStartupInfo->lpReserved2;
    startupInfo.hStdInput = lpStartupInfo->hStdInput;
    startupInfo.hStdOutput = lpStartupInfo->hStdOutput;
    startupInfo.hStdError = lpStartupInfo->hStdError;

    BOOL result = Detoured_CreateProcessW(
        applicationName,
        commandLine.GetMutableString(),
        lpProcessAttributes,
        lpThreadAttributes,
        bInheritHandles,
        dwCreationFlags,
        lpEnvironment,
        currentDirectory,
        &startupInfo,
        lpProcessInformation);

    return result;
}

static bool TryGetUsn(
    _In_    HANDLE handle, 
    _Inout_ USN&   usn,
    _Inout_ DWORD& error)
{
    // TODO: http://msdn.microsoft.com/en-us/library/windows/desktop/aa364993(v=vs.85).aspx says to call GetVolumeInformation to get maximum component length. 
    const size_t MaximumComponentLength = 255;
    const size_t MaximumChangeJournalRecordSize =
        (MaximumComponentLength * sizeof(WCHAR)
        + sizeof(USN_RECORD) - sizeof(WCHAR));
    union {
        USN_RECORD_V2 usnRecord;
        BYTE reserved[MaximumChangeJournalRecordSize];
    };
    DWORD bytesReturned;
    if (!DeviceIoControl(handle,
        FSCTL_READ_FILE_USN_DATA,
        NULL,
        0,
        &usnRecord,
        MaximumChangeJournalRecordSize,
        &bytesReturned,
        NULL))
    {
        error = GetLastError();
        return false;
    }

    assert(bytesReturned <= MaximumChangeJournalRecordSize);
    assert(bytesReturned == usnRecord.RecordLength);
    assert(2 == usnRecord.MajorVersion);
    usn = usnRecord.Usn;
    return true;
}

// If we are not attached this is not App use of RAM but the OS proess startup side of the world.
extern bool g_isAttached;

IMPLEMENTED(Detoured_CreateFileW)
HANDLE WINAPI Detoured_CreateFileW(
    _In_     LPCWSTR               lpFileName,
    _In_     DWORD                 dwDesiredAccess,
    _In_     DWORD                 dwShareMode,
    _In_opt_ LPSECURITY_ATTRIBUTES lpSecurityAttributes,
    _In_     DWORD                 dwCreationDisposition,
    _In_     DWORD                 dwFlagsAndAttributes,
    _In_opt_ HANDLE                hTemplateFile)
{
    DetouredScope scope;

    // The are potential complication here: How to handle a call to CreateFile with the FILE_FLAG_OPEN_REPARSE_POINT? 
    // Is it a real file access. Some code in Windows (urlmon.dll) inspects reparse points when mapping a path to a particular security "Zone".
    if (scope.Detoured_IsDisabled() || IsNullOrEmptyW(lpFileName) || IsSpecialDeviceName(lpFileName))
    {
        return Real_CreateFileW(
            lpFileName,
            dwDesiredAccess,
            dwShareMode,
            lpSecurityAttributes,
            dwCreationDisposition,
            dwFlagsAndAttributes,
            hTemplateFile);
    }

    DWORD error = ERROR_SUCCESS;

    FileOperationContext opContext(
        L"CreateFile",
        dwDesiredAccess,
        dwShareMode,
        dwCreationDisposition,
        dwFlagsAndAttributes,
        lpFileName);

    PolicyResult policyResult;
    if (!policyResult.Initialize(lpFileName)) 
    {
        policyResult.ReportIndeterminatePolicyAndSetLastError(opContext);
        return INVALID_HANDLE_VALUE;
    }

    // We start with allow / ignore (no access requested) and then restrict based on read / write (maybe both, maybe neither!)
    AccessCheckResult accessCheck(RequestedAccess::None, ResultAction::Allow, ReportLevel::Ignore);
    bool forceReadOnlyForRequestedRWAccess = false;
    if (WantsWriteAccess(dwDesiredAccess)) 
    {
        error = GetLastError();
        accessCheck = policyResult.CheckWriteAccess();

        if (ForceReadOnlyForRequestedReadWrite() && accessCheck.ResultAction != ResultAction::Allow) 
        {
            // If ForceReadOnlyForRequestedReadWrite() is true, then we allow read for requested read-write access so long as the tool is allowed to read.
            // In such a case, we change the desired access to read only (see the call to Real_CreateFileW below).
            // As a consequence, the tool can fail if it indeed wants to write to the file.
            if (WantsReadAccess(dwDesiredAccess) && policyResult.AllowRead())
            {
                accessCheck = AccessCheckResult(RequestedAccess::Read, ResultAction::Allow, ReportLevel::Ignore);
                FileOperationContext operationContext(
                    L"ChangedReadWriteToReadAccess",
                    dwDesiredAccess,
                    dwShareMode,
                    dwCreationDisposition,
                    dwFlagsAndAttributes,
                    lpFileName);

                ReportFileAccess(
                    operationContext,
                    FileAccessStatus_Allowed,
                    policyResult,
                    AccessCheckResult(RequestedAccess::None, ResultAction::Deny, ReportLevel::Report),
                    0,
                    -1);

                forceReadOnlyForRequestedRWAccess = true;
            }
        }

        if (!forceReadOnlyForRequestedRWAccess && accessCheck.ShouldDenyAccess()) 
        {
            DWORD denyError = accessCheck.DenialError();
            ReportIfNeeded(accessCheck, opContext, policyResult, denyError); // We won't make it to the post-read-check report below.
            accessCheck.SetLastErrorToDenialError();
            return INVALID_HANDLE_VALUE;
        }

        SetLastError(error);
    }

    // At this point and beyond, we know we are either dealing with a write request that has been approved, or a
    // read request which may or may not have been approved (due to special exceptions for directories and non-existent files).
    // It is safe to go ahead and perform the real CreateFile() call, and then to reason about the results after the fact.

    // Note that we need to add FILE_SHARE_DELETE to dwShareMode to leverage NTFS hardlinks to avoid copying cache
    // content, i.e., we need to be able to delete one of many links to a file. Unfortunately, share-mode is aggregated only per file
    // rather than per-link, so in order to keep unused links delete-able, we should ensure in-use links are delete-able as well.
    // However, adding FILE_SHARE_DELETE may be unexpected, for example, some unit tests may test for sharing violation. Thus,
    // we only add FILE_SHARE_DELETE if the file is tracked.
    
    // We also add FILE_SHARE_READ when it is safe to do so, since some tools accidentally ask for exclusive access on their inputs.

    DWORD desiredAccess = dwDesiredAccess;
    DWORD sharedAccess = dwShareMode;

    if (!policyResult.IndicateUntracked())
    {
        DWORD readSharingIfNeeded = policyResult.ShouldForceReadSharing(accessCheck) ? FILE_SHARE_READ : 0UL;
        desiredAccess = !forceReadOnlyForRequestedRWAccess ? desiredAccess : (desiredAccess & FILE_GENERIC_READ);
        sharedAccess = sharedAccess | readSharingIfNeeded | FILE_SHARE_DELETE;
    }

    error = ERROR_SUCCESS;

    HANDLE handle = Real_CreateFileW(
        lpFileName,
        desiredAccess,
        sharedAccess,
        lpSecurityAttributes,
        dwCreationDisposition,
        dwFlagsAndAttributes,
        hTemplateFile);

    error = GetLastError();

    if (!IgnoreReparsePoints() && IsReparsePoint(lpFileName) && !WantsProbeOnlyAccess(dwDesiredAccess))
    {
        // (1) Reparse point should not be ignored.
        // (2) File/Directory is a reparse point.
        // (3) Desired access is not probe only.
        // Note that handle can be invalid because users can CreateFileW of a symlink whose target is non-existent.

        // Even though the process CreateFile the file with FILE_FLAG_OPEN_REPARSE_POINT, we need to follow the chain of symlinks
        // because the process may use the handle returned by that CreateFile to read the file, which essentially read the final target of symlinks.

        bool accessResult = EnforceChainOfReparsePointAccesses(
            policyResult.GetCanonicalizedPath(),
            (dwFlagsAndAttributes & FILE_FLAG_OPEN_REPARSE_POINT) != 0 ? handle : INVALID_HANDLE_VALUE,
            desiredAccess,
            sharedAccess,
            dwCreationDisposition,
            dwFlagsAndAttributes,
            false);

        if (!accessResult)
        {
            // If we don't have access to the target, close the handle to the reparse point.
            // This way we don't have a leaking handle.
            // (See below we the same when a normal file access is not allowed and close the file.)
            CloseHandle(handle);
            return INVALID_HANDLE_VALUE;
        }
    }

    FileReadContext readContext;
    readContext.InferExistenceFromError(error);

    // Note that 'handle' is allowed invalid for this check. Some tools poke at directories without
    // FILE_FLAG_BACKUP_SEMANTICS and so get INVALID_HANDLE_VALUE / ERROR_ACCESS_DENIED. In that kind of
    // case we have a fallback to re-probe. See function remarks.
    // We skip this to avoid the fallback probe if we don't believe the path exists, since increasing failed-probe volume is dangerous for perf.
    readContext.OpenedDirectory = (readContext.FileExistence == FileExistence::Existent) && IsHandleOrPathToDirectory(handle, lpFileName, false);

    if (WantsReadAccess(dwDesiredAccess)) 
    {
        // We've now established all of the read context, which can further inform the access decision.
        // (e.g. maybe we we allow read only if the file doesn't exist).
        accessCheck = AccessCheckResult::Combine(accessCheck, policyResult.CheckReadAccess(RequestedReadAccess::Read, readContext));
    }
    else if (WantsProbeOnlyAccess(dwDesiredAccess)) 
    {
        accessCheck = AccessCheckResult::Combine(accessCheck, policyResult.CheckReadAccess(RequestedReadAccess::Probe, readContext));
    }

    // Additionally, for files (not directories) we can enforce a USN match (or report).
    bool unexpectedUsn = false;
    bool reportUsn = false;
    USN usn = -1; // -1, or 0xFFFFFFFFFFFFFFFF indicates that USN could/was not obtained
    if (!readContext.OpenedDirectory) // We do not want to report accesses to directories.
    {
        reportUsn = handle != INVALID_HANDLE_VALUE && policyResult.ReportUsnAfterOpen();
        bool checkUsn = handle != INVALID_HANDLE_VALUE && policyResult.GetExpectedUsn() != -1;

        DWORD getUsnError = ERROR_SUCCESS;
        if ((reportUsn || checkUsn) && !TryGetUsn(handle, /* inout */ usn, /* inout */ getUsnError))
        {
            WriteWarningOrErrorF(L"Could not obtain USN for file path '%s'. Error: %d",
                policyResult.GetCanonicalizedPath().GetPathString(), getUsnError);
            MaybeBreakOnAccessDenied();

            ReportFileAccess(
                opContext,
                FileAccessStatus_CannotDeterminePolicy,
                policyResult,
                AccessCheckResult(RequestedAccess::None, ResultAction::Deny, ReportLevel::Report),
                getUsnError,
                usn);

            if (handle != INVALID_HANDLE_VALUE) 
            {
                CloseHandle(handle);
            }

            SetLastError(ERROR_ACCESS_DENIED);
            return INVALID_HANDLE_VALUE;
        }

        if (checkUsn && usn != policyResult.GetExpectedUsn())
        {
            WriteWarningOrErrorF(L"USN mismatch.  Actual USN: 0x%08x, expected USN: 0x%08x.",
                policyResult.GetCanonicalizedPath().GetPathString(), usn, policyResult.GetExpectedUsn());
            unexpectedUsn = true;
        }
    }

    // ReportUsnAfterOpen implies reporting.
    // TODO: Would be cleaner to just use the normal Report flags (per file / scope) and a global 'look at USNs' flag.
    // Additionally, we report (but do never deny) if a USN did not match an expectation. We must be tolerant to USN changes
    // (which the consumer of these reports may interpret) due to e.g. hard link changes (when a link is added or removed to a file).
    if (reportUsn || unexpectedUsn) 
    {
        accessCheck.ReportLevel = ReportLevel::ReportExplicit;
        accessCheck = AccessCheckResult::Combine(accessCheck, accessCheck.With(ReportLevel::ReportExplicit));
    }

    ReportIfNeeded(accessCheck, opContext, policyResult, error, usn);

    // It is possible that we only reached a deny action under some access check combinations above (rather than a direct check),
    // so log and maybe break here as well now that it is final.
    if (accessCheck.ResultAction != ResultAction::Allow) 
    {
        WriteWarningOrErrorF(L"Access to file path '%s' is denied.  Requested access: 0x%08x, policy allows: 0x%08x.",
            policyResult.GetCanonicalizedPath().GetPathString(), dwDesiredAccess, policyResult.GetPolicy());
        MaybeBreakOnAccessDenied();
    }

    if (accessCheck.ShouldDenyAccess()) 
    {
        error = accessCheck.DenialError();

        if (handle != INVALID_HANDLE_VALUE) 
        {
            CloseHandle(handle);
        }

        handle = INVALID_HANDLE_VALUE;
    }
    else if (handle != INVALID_HANDLE_VALUE) 
    {
        HandleType handleType = readContext.OpenedDirectory ? HandleType::Directory : HandleType::File;
        RegisterHandleOverlay(handle, accessCheck, policyResult, handleType);
    }

    // Propagate the correct error code to the caller.
    SetLastError(error);
    return handle;
}

IMPLEMENTED(Detoured_CloseHandle)
BOOL WINAPI Detoured_CloseHandle(_In_ HANDLE handle)
{
    DetouredScope scope;

    if (scope.Detoured_IsDisabled() || IsNullOrInvalidHandle(handle))
    {
        return Real_CloseHandle(handle);
    }

    // Make sure the handle is closed after the object is removed from the map.
    // This way the handle will never be assigned to a another object before removed from the table.
    CloseHandleOverlay(handle , true); 

    return Real_CloseHandle(handle);
}

IMPLEMENTED(Detoured_CreateFileA)
HANDLE WINAPI Detoured_CreateFileA(
    _In_     LPCSTR                lpFileName,
    _In_     DWORD                 dwDesiredAccess,
    _In_     DWORD                 dwShareMode,
    _In_opt_ LPSECURITY_ATTRIBUTES lpSecurityAttributes,
    _In_     DWORD                 dwCreationDisposition,
    _In_     DWORD                 dwFlagsAndAttributes,
    _In_opt_ HANDLE                hTemplateFile)
{
    {
        DetouredScope scope;
        if (scope.Detoured_IsDisabled() || IsNullOrEmptyA(lpFileName))
        {
            return Real_CreateFileA(
                lpFileName,
                dwDesiredAccess,
                dwShareMode,
                lpSecurityAttributes,
                dwCreationDisposition,
                dwFlagsAndAttributes,
                hTemplateFile);
        }
    }

    UnicodeConverter fileName(lpFileName);
    return Detoured_CreateFileW(
        fileName,
        dwDesiredAccess,
        dwShareMode,
        lpSecurityAttributes,
        dwCreationDisposition,
        dwFlagsAndAttributes,
        hTemplateFile);
}

// Detoured_GetVolumePathNameW
//
// There's no need to check lpszFileName for null because we are not applying
// any BuildXL policy in this function. There's no reason to check for whether
// lpszFileName is the empty string because although the function fails,
// the last error is set to ERROR_SUCCESS.
//
// Note: There is no need to detour GetVolumePathNameA because there is no policy to apply.
IMPLEMENTED(Detoured_GetVolumePathNameW)
BOOL WINAPI Detoured_GetVolumePathNameW(
    _In_  LPCWSTR lpszFileName,
    _Out_ LPWSTR  lpszVolumePathName,
    _In_  DWORD   cchBufferLength
    )
{
    // The reason for this scope check is that GetVolumePathNameW calls many other detoured APIs.
    // We do not need to have any reports for file accesses from these APIs, because thay are not what the application called.
    // (It was purely inserted by us.)

    DetouredScope scope;
    return Real_GetVolumePathNameW(lpszFileName, lpszVolumePathName, cchBufferLength);
}

IMPLEMENTED(Detoured_GetFileAttributesW)
DWORD WINAPI Detoured_GetFileAttributesW(_In_  LPCWSTR lpFileName)
{
    DetouredScope scope;
    if (scope.Detoured_IsDisabled() || IsNullOrEmptyW(lpFileName) || IsSpecialDeviceName(lpFileName))
    {
#pragma warning(suppress: 6387)
        return Real_GetFileAttributesW(lpFileName);
    }

    FileOperationContext fileOperationContext = FileOperationContext::CreateForRead(L"GetFileAttributes", lpFileName);

    PolicyResult policyResult;
    if (!policyResult.Initialize(lpFileName)) 
    {
        policyResult.ReportIndeterminatePolicyAndSetLastError(fileOperationContext);
        return INVALID_FILE_ATTRIBUTES;
    }

    DWORD attributes = INVALID_FILE_ATTRIBUTES;
    DWORD error = ERROR_SUCCESS;
    
    attributes = Real_GetFileAttributesW(lpFileName);

    if (attributes == INVALID_FILE_ATTRIBUTES) 
    {
        error = GetLastError();
    }

    // Now we can make decisions based on the file's existence and type.
    FileReadContext fileReadContext;
    fileReadContext.InferExistenceFromError(error);
    fileReadContext.OpenedDirectory = attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0;

    AccessCheckResult accessCheck = policyResult.CheckReadAccess(RequestedReadAccess::Probe, fileReadContext);
    ReportIfNeeded(accessCheck, fileOperationContext, policyResult, error);

    // No need to enforce chain of reparse point accesess because if the path points to a symbolic link,
    // then GetFileAttributes returns attributes for the symbolic link.
    if (accessCheck.ShouldDenyAccess()) 
    {
        error = accessCheck.DenialError();
        attributes = INVALID_FILE_ATTRIBUTES;
    }

    SetLastError(error);
    return attributes;
}

IMPLEMENTED(Detoured_GetFileAttributesA)
DWORD WINAPI Detoured_GetFileAttributesA(_In_  LPCSTR lpFileName)
{
    {
        DetouredScope scope;
        if (scope.Detoured_IsDisabled() || IsNullOrEmptyA(lpFileName))
        {
#pragma warning(suppress: 6387)
            return Real_GetFileAttributesA(lpFileName);
        }
    }

    UnicodeConverter unicodePath(lpFileName);
    return Detoured_GetFileAttributesW(unicodePath);
}

IMPLEMENTED(Detoured_GetFileAttributesExW)
BOOL WINAPI Detoured_GetFileAttributesExW(
    _In_  LPCWSTR                lpFileName,
    _In_  GET_FILEEX_INFO_LEVELS fInfoLevelId,
    _Out_ LPVOID                 lpFileInformation)
{
    DetouredScope scope;
    if (scope.Detoured_IsDisabled() || IsNullOrEmptyW(lpFileName) || IsSpecialDeviceName(lpFileName))
    {
        return Real_GetFileAttributesExW(lpFileName, fInfoLevelId, lpFileInformation);
    }

    FileOperationContext fileOperationContext = FileOperationContext::CreateForRead(L"GetFileAttributesEx", lpFileName);

    PolicyResult policyResult;
    if (!policyResult.Initialize(lpFileName)) 
    {
        policyResult.ReportIndeterminatePolicyAndSetLastError(fileOperationContext);

        lpFileInformation = nullptr;
        return FALSE;
    }

    DWORD error = ERROR_SUCCESS;
    BOOL querySucceeded = TRUE;
    // We could be clever and avoid calling this when already doomed to failure. However:
    // - Unlike CreateFile, this query can't interfere with other processes
    // - We want lpFileInformation to be zeroed according to whatever policy GetFileAttributesEx has.
    querySucceeded = Real_GetFileAttributesExW(lpFileName, fInfoLevelId, lpFileInformation);
    if (!querySucceeded) 
    {
        error = GetLastError();
    }

    WIN32_FILE_ATTRIBUTE_DATA* fileStandardInfo = (fInfoLevelId == GetFileExInfoStandard && lpFileInformation != nullptr) ?
        (WIN32_FILE_ATTRIBUTE_DATA*)lpFileInformation : nullptr;

    // Now we can make decisions based on existence and type.
    FileReadContext fileReadContext;
    fileReadContext.InferExistenceFromError(error);
    fileReadContext.OpenedDirectory = querySucceeded && fileStandardInfo != nullptr &&
        (fileStandardInfo->dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;

    AccessCheckResult accessCheck = policyResult.CheckReadAccess(RequestedReadAccess::Probe, fileReadContext);
    ReportIfNeeded(accessCheck, fileOperationContext, policyResult, error);

    // No need to enforce chain of reparse point accesess because if the path points to a symbolic link,
    // then GetFileAttributes returns attributes for the symbolic link.
    if (accessCheck.ShouldDenyAccess()) 
    {
        error = accessCheck.DenialError();
        querySucceeded = FALSE;
    }

    if (querySucceeded && policyResult.ShouldOverrideTimestamps(accessCheck) && fileStandardInfo != nullptr) 
    {
#if SUPER_VERBOSE
        Dbg(L"GetFileAttributesExW: Overriding timestamps for %s", policyResult.GetCanonicalizedPath().GetPathString());
#endif // SUPER_VERBOSE
        OverrideTimestampsForInputFile(fileStandardInfo);
    }

    SetLastError(error);
    return querySucceeded;
}

IMPLEMENTED(Detoured_GetFileAttributesExA)
BOOL WINAPI Detoured_GetFileAttributesExA(
    _In_  LPCSTR                 lpFileName,
    _In_  GET_FILEEX_INFO_LEVELS fInfoLevelId,
    _Out_ LPVOID                 lpFileInformation)
{
    {
        DetouredScope scope;
        if (scope.Detoured_IsDisabled() || IsNullOrEmptyA(lpFileName))
        {
            return Real_GetFileAttributesExA(
                lpFileName,
                fInfoLevelId,
                lpFileInformation);
        }
    }

    UnicodeConverter unicodePath(lpFileName);
    return Detoured_GetFileAttributesExW(
        unicodePath,
        fInfoLevelId,
        lpFileInformation);
}

// Detoured_CopyFileW
//
// lpExistingFileName is the source file. We require read access to this location.
// lpNewFileName is the destination file. We require write access to this location (as we create it).
//
// Don't worry about bFailIfExists, that will all be handled by the actual API and doesn't affect
// our policy.
//
// Note: Does NOT operate on directories.
IMPLEMENTED(Detoured_CopyFileW)
BOOL WINAPI Detoured_CopyFileW(
    _In_ LPCWSTR lpExistingFileName,
    _In_ LPCWSTR lpNewFileName,
    _In_ BOOL bFailIfExists
    )
{
    // Don't duplicate complex access-policy logic between CopyFileEx and CopyFile.
    // This forwarder is identical to the internal implementation of CopyFileExW
    // so it should be safe to always forward at our level.
    return Detoured_CopyFileExW(
        lpExistingFileName,
        lpNewFileName,
        (LPPROGRESS_ROUTINE)NULL,
        (LPVOID)NULL,
        (LPBOOL)NULL,
        bFailIfExists ? (DWORD)COPY_FILE_FAIL_IF_EXISTS : 0);
}

IMPLEMENTED(Detoured_CopyFileA)
BOOL WINAPI Detoured_CopyFileA(
    _In_ LPCSTR lpExistingFileName,
    _In_ LPCSTR lpNewFileName,
    _In_ BOOL   bFailIfExists)
{
    {
        DetouredScope scope;
        if (scope.Detoured_IsDisabled() || IsNullOrEmptyA(lpExistingFileName) || IsNullOrEmptyA(lpNewFileName))
        {
            return Real_CopyFileA(
                lpExistingFileName,
                lpNewFileName,
                bFailIfExists);
        }
    }

    UnicodeConverter existingFileName(lpExistingFileName);
    UnicodeConverter newFileName(lpNewFileName);
    return Detoured_CopyFileW(
        existingFileName,
        newFileName,
        bFailIfExists);
}

IMPLEMENTED(Detoured_CopyFileExW)
BOOL WINAPI Detoured_CopyFileExW(
    _In_     LPCWSTR            lpExistingFileName,
    _In_     LPCWSTR            lpNewFileName,
    _In_opt_ LPPROGRESS_ROUTINE lpProgressRoutine,
    _In_opt_ LPVOID             lpData,
    _In_opt_ LPBOOL             pbCancel,
    _In_     DWORD              dwCopyFlags)
{
    DetouredScope scope;
    if (scope.Detoured_IsDisabled() || 
        IsNullOrEmptyW(lpExistingFileName) || 
        IsNullOrEmptyW(lpNewFileName) || 
        IsSpecialDeviceName(lpExistingFileName) ||
        IsSpecialDeviceName(lpNewFileName))
    {
        return Real_CopyFileExW(
            lpExistingFileName,
            lpNewFileName,
            lpProgressRoutine,
            lpData,
            pbCancel,
            dwCopyFlags);
    }

    FileOperationContext sourceOpContext = FileOperationContext::CreateForRead(L"CopyFile_Source", lpExistingFileName);
    PolicyResult sourcePolicyResult;
    if (!sourcePolicyResult.Initialize(lpExistingFileName))
    {
        sourcePolicyResult.ReportIndeterminatePolicyAndSetLastError(sourceOpContext);
        return FALSE;
    }

    FileOperationContext destinationOpContext = FileOperationContext(
        L"CopyFile_Dest",
        GENERIC_WRITE,
        0,
        CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL,
        lpNewFileName);
    PolicyResult destPolicyResult;
    if (!destPolicyResult.Initialize(lpNewFileName))
    {
        destPolicyResult.ReportIndeterminatePolicyAndSetLastError(destinationOpContext);
        return FALSE;
    }

    // When COPY_FILE_COPY_SYMLINK is specified, then no need to enforce chain of symlink accesses.
    if ((dwCopyFlags & COPY_FILE_COPY_SYMLINK) == 0 &&
        !EnforceChainOfReparsePointAccessesForNonCreateFile(sourceOpContext))
    {
        return FALSE;
    }

    // Writes are destructive, before doing a copy we ensure that write access is definitely allowed.

    AccessCheckResult destAccessCheck = destPolicyResult.CheckWriteAccess();
    if (destAccessCheck.ShouldDenyAccess()) 
    {
        DWORD denyError = destAccessCheck.DenialError();
        ReportIfNeeded(destAccessCheck, destinationOpContext, destPolicyResult, denyError);
        destAccessCheck.SetLastErrorToDenialError();
        return FALSE;
    }

    // Now we can safely try to copy, but note that the corresponding read of the source file may end up disallowed
    // (maybe the source file exists, as CopyFileW requires, but we only allow non-existence probes for this path).

    DWORD error = ERROR_SUCCESS;
    BOOL result = Real_CopyFileExW(
        lpExistingFileName,
        lpNewFileName,
        lpProgressRoutine,
        lpData,
        pbCancel,
        dwCopyFlags);

    if (!result) 
    {
        error = GetLastError();
    }

    FileReadContext sourceReadContext;
    sourceReadContext.OpenedDirectory = false; // TODO: Perhaps CopyFile fails with a nice error code in this case.
    sourceReadContext.InferExistenceFromError(error);

    AccessCheckResult sourceAccessCheck = sourcePolicyResult.CheckReadAccess(RequestedReadAccess::Read, sourceReadContext);

    ReportIfNeeded(sourceAccessCheck, sourceOpContext, sourcePolicyResult, error);
    ReportIfNeeded(destAccessCheck, destinationOpContext, destPolicyResult, error);

    if (sourceAccessCheck.ShouldDenyAccess()) 
    {
        result = FALSE;
        error = sourceAccessCheck.DenialError();
    }

    SetLastError(error);
    return result;
}

IMPLEMENTED(Detoured_CopyFileExA)
BOOL WINAPI Detoured_CopyFileExA(
    _In_     LPCSTR             lpExistingFileName,
    _In_     LPCSTR             lpNewFileName,
    _In_opt_ LPPROGRESS_ROUTINE lpProgressRoutine,
    _In_opt_ LPVOID             lpData,
    _In_opt_ LPBOOL             pbCancel,
    _In_     DWORD              dwCopyFlags)
{
    {
        DetouredScope scope;
        if (scope.Detoured_IsDisabled() || IsNullOrEmptyA(lpExistingFileName) || IsNullOrEmptyA(lpNewFileName))
        {
            return Real_CopyFileExA(
                lpExistingFileName,
                lpNewFileName,
                lpProgressRoutine,
                lpData,
                pbCancel,
                dwCopyFlags);
        }
    }

    UnicodeConverter existingFileName(lpExistingFileName);
    UnicodeConverter newFileName(lpNewFileName);
    return Detoured_CopyFileExW(
        existingFileName,
        newFileName,
        lpProgressRoutine,
        lpData,
        pbCancel,
        dwCopyFlags);
}

// Below are detours of various Move functions. Looking up the actual
// implementation of these functions, one finds that they are all wrappers
// around the MoveFileWithProgress.
//
// MoveFile(a, b) => MoveFileWithProgress(a, b, NULL, NULL, MOVEFILE_COPY_ALLOWED)
// MoveFileEx(a, b, flags) => MoveFileWithProgress(a, b, NULL, NULL, flags)
//
IMPLEMENTED(Detoured_MoveFileW)
BOOL WINAPI Detoured_MoveFileW(
    _In_ LPCWSTR lpExistingFileName,
    _In_ LPCWSTR lpNewFileName)
{
    return Detoured_MoveFileWithProgressW(
        lpExistingFileName,
        lpNewFileName,
        (LPPROGRESS_ROUTINE)NULL,
        NULL,
        MOVEFILE_COPY_ALLOWED);
}

IMPLEMENTED(Detoured_MoveFileA)
BOOL WINAPI Detoured_MoveFileA(
    _In_ LPCSTR lpExistingFileName,
    _In_ LPCSTR lpNewFileName)
{
    {
        DetouredScope scope;
        if (scope.Detoured_IsDisabled() || IsNullOrEmptyA(lpExistingFileName) || IsNullOrEmptyA(lpNewFileName)) 
        {
            return Real_MoveFileA(
                lpExistingFileName,
                lpNewFileName);
        }
    }

    UnicodeConverter existingFileName(lpExistingFileName);
    UnicodeConverter newFileName(lpNewFileName);

    return Detoured_MoveFileWithProgressW(
        existingFileName,
        newFileName,
        (LPPROGRESS_ROUTINE)NULL,
        NULL,
        MOVEFILE_COPY_ALLOWED);
}

IMPLEMENTED(Detoured_MoveFileExW)
BOOL WINAPI Detoured_MoveFileExW(
    _In_     LPCWSTR lpExistingFileName,
    _In_opt_ LPCWSTR lpNewFileName,
    _In_     DWORD   dwFlags)
{
    return Detoured_MoveFileWithProgressW(
        lpExistingFileName,
        lpNewFileName,
        (LPPROGRESS_ROUTINE)NULL,
        NULL,
        dwFlags);
}

IMPLEMENTED(Detoured_MoveFileExA)
BOOL WINAPI Detoured_MoveFileExA(
    _In_      LPCSTR lpExistingFileName,
    _In_opt_  LPCSTR lpNewFileName,
    _In_      DWORD  dwFlags)
{
    {
        DetouredScope scope;
        if (scope.Detoured_IsDisabled() || IsNullOrEmptyA(lpExistingFileName) || IsNullOrEmptyA(lpNewFileName)) 
        {
            return Real_MoveFileExA(
                lpExistingFileName,
                lpNewFileName,
                dwFlags);
        }
    }

    UnicodeConverter existingFileName(lpExistingFileName);
    UnicodeConverter newFileName(lpNewFileName);

    return Detoured_MoveFileWithProgressW(
        existingFileName,
        newFileName,
        (LPPROGRESS_ROUTINE)NULL,
        NULL,
        dwFlags);
}

// Detoured_MoveFileWithProgressW
//
// lpExistingFileName is the source file. We require write access to this location (as we effectively delete it).
// lpNewFileName is the destination file. We require write access to this location (as we create it).
//
// lpNewFileName is optional in this API but if is NULL then this API allows the file to be deleted
// (following a reboot). See the excerpt from the documentation below:
//
// "If dwFlags specifies MOVEFILE_DELAY_UNTIL_REBOOT and lpNewFileName is NULL,
// MoveFileEx registers the lpExistingFileName file to be deleted when the
// system restarts."
IMPLEMENTED(Detoured_MoveFileWithProgressW)
BOOL WINAPI Detoured_MoveFileWithProgressW(
    _In_      LPCWSTR            lpExistingFileName,
    _In_opt_  LPCWSTR            lpNewFileName,
    _In_opt_  LPPROGRESS_ROUTINE lpProgressRoutine,
    _In_opt_  LPVOID             lpData,
    _In_      DWORD              dwFlags)
{
    DetouredScope scope;
    if (scope.Detoured_IsDisabled() 
        || IsNullOrEmptyW(lpExistingFileName) 
        || IsNullOrEmptyW(lpNewFileName) 
        || IsSpecialDeviceName(lpExistingFileName) 
        || IsSpecialDeviceName(lpNewFileName)) 
    {
        return Real_MoveFileWithProgressW(
            lpExistingFileName,
            lpNewFileName,
            lpProgressRoutine,
            lpData,
            dwFlags);
    }

    FileOperationContext sourceOpContext = FileOperationContext(
        L"MoveFileWithProgress_Source",
        GENERIC_READ | DELETE,
        0,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        lpExistingFileName);

    PolicyResult sourcePolicyResult;
    if (!sourcePolicyResult.Initialize(lpExistingFileName)) 
    {
        sourcePolicyResult.ReportIndeterminatePolicyAndSetLastError(sourceOpContext);
        return FALSE;
    }

    // When MOVEFILE_COPY_ALLOWED is set, If the file is to be moved to a different volume, then the function simulates 
    // the move by using the CopyFile and DeleteFile functions. In moving symlink using MOVEFILE_COPY_ALLOWED flag, 
    // the call to CopyFile function passes COPY_FILE_SYMLINK, which makes the CopyFile function copies the symlink itself
    // instead of the (final) target of the symlink.

    FileOperationContext destinationOpContext = FileOperationContext(
        L"MoveFileWithProgress_Dest",
        GENERIC_WRITE,
        0,
        CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL,
        lpNewFileName == NULL ? L"" : lpNewFileName);

    PolicyResult destPolicyResult;

    if (lpNewFileName != NULL && !destPolicyResult.Initialize(lpNewFileName)) 
    {
        destPolicyResult.ReportIndeterminatePolicyAndSetLastError(destinationOpContext);
        return FALSE;
    }

    // Writes are destructive. Before doing a move we ensure that write access is definitely allowed to the source (read and delete) and destination (write).

    AccessCheckResult sourceAccessCheck = sourcePolicyResult.CheckWriteAccess();

    if (sourceAccessCheck.ShouldDenyAccess()) 
    {
        // We report the source access here since we are returning early. Otherwise it is deferred until post-read.
        DWORD denyError = sourceAccessCheck.DenialError();
        ReportIfNeeded(sourceAccessCheck, sourceOpContext, sourcePolicyResult, denyError);
        sourceAccessCheck.SetLastErrorToDenialError();
        return FALSE;
    }

    AccessCheckResult destAccessCheck(RequestedAccess::Write, ResultAction::Allow, ReportLevel::Ignore);

    if (!destPolicyResult.IsIndeterminate()) 
    {
        // PolicyResult::CheckWriteAccess gives the same result for writing a file or creating a directory.
        // Thus, we don't need to call PolicyResult::CheckCreateDirectoryAccess.
        destAccessCheck = destPolicyResult.CheckWriteAccess();

        if (destAccessCheck.ShouldDenyAccess()) 
        {
            // We report the destination access here since we are returning early. Otherwise it is deferred until post-read.
            DWORD denyError = destAccessCheck.DenialError();
            ReportIfNeeded(destAccessCheck, destinationOpContext, destPolicyResult, denyError);
            destAccessCheck.SetLastErrorToDenialError();
            return FALSE;
        }
    }

    bool moveDirectory = false;
    vector<ReportData> filesAndDirectoriesToReport;

    if (IsPathToDirectory(lpExistingFileName, true))
    {
        moveDirectory = true;

        // Verify move directory.
        // The destination of move directory must be on the same drive.
        if (!ValidateMoveDirectory(
                L"MoveFileWithProgress_Source",
                L"MoveFileWithProgress_Dest", 
                lpExistingFileName, 
                lpNewFileName, 
                filesAndDirectoriesToReport))
        {
            return FALSE;
        }
    }
    else if ((dwFlags & MOVEFILE_COPY_ALLOWED) != 0)
    {
        // Copy can be performed, and thus file will be read, but copy cannot be moving directory.
        sourceAccessCheck = AccessCheckResult::Combine(
            sourceAccessCheck,
            sourcePolicyResult.CheckReadAccess(RequestedReadAccess::Read, FileReadContext(FileExistence::Existent, false)));

        if (sourceAccessCheck.ShouldDenyAccess())
        {
            DWORD denyError = sourceAccessCheck.DenialError();
            ReportIfNeeded(sourceAccessCheck, sourceOpContext, sourcePolicyResult, denyError);
            sourceAccessCheck.SetLastErrorToDenialError();
            return FALSE;
        }
    }

    // It's now safe to perform the move, which should tell us the existence of the source side (and so, if it may be read or not).

    DWORD error = ERROR_SUCCESS;
    BOOL result = Real_MoveFileWithProgressW(
        lpExistingFileName,
        lpNewFileName,
        lpProgressRoutine,
        lpData,
        dwFlags);

    if (!result) 
    {
        error = GetLastError();
    }

    ReportIfNeeded(sourceAccessCheck, sourceOpContext, sourcePolicyResult, error);
    ReportIfNeeded(destAccessCheck, destinationOpContext, destPolicyResult, error);

    if (moveDirectory) 
    {
        for (vector<ReportData>::const_iterator it = filesAndDirectoriesToReport.cbegin(); it != filesAndDirectoriesToReport.cend(); ++it) 
        {
            ReportIfNeeded(it->GetAccessCheckResult(), it->GetFileOperationContext(), it->GetPolicyResult(), error);
        }
    }
    
    SetLastError(error);

    return result;
}

IMPLEMENTED(Detoured_MoveFileWithProgressA)
BOOL WINAPI Detoured_MoveFileWithProgressA(
    _In_     LPCSTR             lpExistingFileName,
    _In_opt_ LPCSTR             lpNewFileName,
    _In_opt_ LPPROGRESS_ROUTINE lpProgressRoutine,
    _In_opt_ LPVOID             lpData,
    _In_     DWORD              dwFlags)
{
    {
        DetouredScope scope;
        if (scope.Detoured_IsDisabled() || IsNullOrEmptyA(lpExistingFileName))
        {
            return Real_MoveFileWithProgressA(
                lpExistingFileName,
                lpNewFileName,
                lpProgressRoutine,
                lpData,
                dwFlags);
        }
    }

    UnicodeConverter existingFileName(lpExistingFileName);
    UnicodeConverter newFileName(lpNewFileName);
    return Detoured_MoveFileWithProgressW(
        existingFileName,
        newFileName,
        lpProgressRoutine,
        lpData,
        dwFlags);
}

BOOL WINAPI Detoured_ReplaceFileW(
    _In_       LPCWSTR lpReplacedFileName,
    _In_       LPCWSTR lpReplacementFileName,
    _In_opt_   LPCWSTR lpBackupFileName,
    _In_       DWORD   dwReplaceFlags,
    __reserved LPVOID  lpExclude,
    __reserved LPVOID  lpReserved)
{
    // TODO:implement detours logic
    return Real_ReplaceFileW(
        lpReplacedFileName,
        lpReplacementFileName,
        lpBackupFileName,
        dwReplaceFlags,
        lpExclude,
        lpReserved);
}

BOOL WINAPI Detoured_ReplaceFileA(
    _In_        LPCSTR lpReplacedFileName,
    _In_        LPCSTR lpReplacementFileName,
    _In_opt_    LPCSTR lpBackupFileName,
    _In_        DWORD dwReplaceFlags,
    __reserved  LPVOID lpExclude,
    __reserved  LPVOID lpReserved)
{
    {
        DetouredScope scope;
        if (scope.Detoured_IsDisabled() 
            || IsNullOrEmptyA(lpReplacedFileName) 
            || IsNullOrEmptyA(lpReplacementFileName))
        {
            return Real_ReplaceFileA(
                lpReplacedFileName,
                lpReplacementFileName,
                lpBackupFileName,
                dwReplaceFlags,
                lpExclude,
                lpReserved);
        }
    }

    UnicodeConverter replacedFileName(lpReplacedFileName);
    UnicodeConverter replacementFileName(lpReplacementFileName);
    UnicodeConverter backupFileName(lpBackupFileName);

    return Detoured_ReplaceFileW(
        replacedFileName,
        replacementFileName,
        backupFileName,
        dwReplaceFlags,
        lpExclude,
        lpReserved);
}

/// <summary>
/// Performs a read-only probe of a path to simulate a read-only variant of DeleteFile (if the target filename does not exist, DeleteFile is like a generic read probe).
/// </summary>
/// <remarks>
/// If the read-only probe indicates that DeleteFile would have attempted to write, instead writeAccessCheck is returned (requested access is Write).
/// Otherwise, a Probe-level access check is returned (which may or may not be permitted, based on policy).
///
/// In all, we want the treatement of DeleteFile to be equivalent to the following separable accesses:
/// <code>
/// atomic {
///   if (Probe(path) == Exists) { Write() } else { fail }
/// }
/// </code>
/// (but we want to report one access, i.e., the Write if it happens otherwise the probe).
/// </remarks>
static AccessCheckResult DeleteFileSafeProbe(AccessCheckResult writeAccessCheck, FileOperationContext const& opContext, PolicyResult const& policyResult, DWORD* probeError) 
{
    
    DWORD attributes = GetFileAttributesW(opContext.NoncanonicalPath);
    *probeError = ERROR_SUCCESS;
    if (attributes == INVALID_FILE_ATTRIBUTES) 
    {
        *probeError = GetLastError();
    }

    FileReadContext probeContext;
    probeContext.OpenedDirectory = attributes != INVALID_FILE_ATTRIBUTES && ((attributes & FILE_ATTRIBUTE_DIRECTORY) != 0);
    probeContext.InferExistenceFromError(*probeError);

    AccessCheckResult probeAccessCheck = policyResult.CheckReadAccess(RequestedReadAccess::Probe, probeContext);

    if (probeContext.FileExistence == FileExistence::Existent) 
    {
        if (probeContext.OpenedDirectory) 
        {
            // This is a probe for an existent directory (DeleteFile fails on directories).
            *probeError = ERROR_ACCESS_DENIED;
        }
        else 
        {
            // This would be the write path, so we fail it.
            probeAccessCheck = AccessCheckResult::Combine(writeAccessCheck, AccessCheckResult::DenyOrWarn(RequestedAccess::Write));
            *probeError = ERROR_ACCESS_DENIED;
        }
    }

    if (probeAccessCheck.ShouldDenyAccess()) 
    {
        *probeError = probeAccessCheck.DenialError();
    }

    return probeAccessCheck;
}

// Detoured_DeleteFileW
//
// lpFileName is the file to be deleted. We require write access to this location (as we effectively delete it).
//
// Note: The DeleteFile API does NOT allow deleting directories, unlike MoveFile does. (Use RemoveDirectory for this.)
IMPLEMENTED(Detoured_DeleteFileW)
BOOL WINAPI Detoured_DeleteFileW(_In_ LPCWSTR lpFileName)
{
    DetouredScope scope;
    if (scope.Detoured_IsDisabled() ||
        IsNullOrEmptyW(lpFileName) ||
        IsSpecialDeviceName(lpFileName)) 
    {
        return Real_DeleteFileW(lpFileName);
    }

    FileOperationContext opContext = FileOperationContext(
        L"DeleteFile",
        DELETE,
        0,
        TRUNCATE_EXISTING,
        FILE_FLAG_DELETE_ON_CLOSE,
        lpFileName);

    PolicyResult policyResult;
    if (!policyResult.Initialize(lpFileName)) 
    {
        policyResult.ReportIndeterminatePolicyAndSetLastError(opContext);
        return FALSE;
    }

    AccessCheckResult accessCheck = policyResult.CheckWriteAccess();

    if (accessCheck.ShouldDenyAccess())
    {
        // Maybe we can re-phrase this as an absent-file or directory probe?
        DWORD probeError;
        AccessCheckResult readAccessCheck = DeleteFileSafeProbe(accessCheck, opContext, policyResult, /*out*/ &probeError);
        ReportIfNeeded(readAccessCheck, opContext, policyResult, probeError);
        SetLastError(probeError);
        return FALSE;
    }

    DWORD error = ERROR_SUCCESS;
    BOOL result = Real_DeleteFileW(lpFileName);
    if (!result) 
    {
        error = GetLastError();
    }

    if (!result && accessCheck.ResultAction != ResultAction::Allow) 
    {
        // On error, we didn't delete anything.
        // We retry as a read just like above; this ensures ResultAction::Warn acts like ResultAction::Deny.
        AccessCheckResult readAccessCheck = DeleteFileSafeProbe(accessCheck, opContext, policyResult, /*out*/ &error);
        ReportIfNeeded(readAccessCheck, opContext, policyResult, error);
    }
    else 
    {
        ReportIfNeeded(accessCheck, opContext, policyResult, error);
    }

    SetLastError(error);
    return result;
}

IMPLEMENTED(Detoured_DeleteFileA)
BOOL WINAPI Detoured_DeleteFileA(_In_ LPCSTR lpFileName)
{
    {
        DetouredScope scope;
        if (scope.Detoured_IsDisabled() || IsNullOrEmptyA(lpFileName))
        {
            return Real_DeleteFileA(lpFileName);
        }
    }

    UnicodeConverter fileName(lpFileName);
    return Detoured_DeleteFileW(fileName);
}

IMPLEMENTED(Detoured_CreateHardLinkW)
BOOL WINAPI Detoured_CreateHardLinkW(
    _In_       LPCWSTR               lpFileName,
    _In_       LPCWSTR               lpExistingFileName,
    __reserved LPSECURITY_ATTRIBUTES lpSecurityAttributes)
{
    DetouredScope scope;
    if (scope.Detoured_IsDisabled() ||
        IsNullOrEmptyW(lpFileName) ||
        IsNullOrEmptyW(lpExistingFileName) ||
        IsSpecialDeviceName(lpFileName) ||
        IsSpecialDeviceName(lpExistingFileName))
    {
        return Real_CreateHardLinkW(
            lpFileName,
            lpExistingFileName,
            lpSecurityAttributes);
    }

    FileOperationContext sourceOpContext = FileOperationContext::CreateForRead(L"CreateHardLink_Source", lpExistingFileName);
    PolicyResult sourcePolicyResult;
    if (!sourcePolicyResult.Initialize(lpExistingFileName))
    {
        sourcePolicyResult.ReportIndeterminatePolicyAndSetLastError(sourceOpContext);
        return FALSE;
    }

    FileOperationContext destinationOpContext = FileOperationContext(
        L"CreateHardLink_Dest",
        GENERIC_WRITE,
        0,
        CREATE_NEW,
        FILE_ATTRIBUTE_NORMAL,
        lpFileName);

    PolicyResult destPolicyResult;
    if (!destPolicyResult.Initialize(lpFileName))
    {
        destPolicyResult.ReportIndeterminatePolicyAndSetLastError(destinationOpContext);
        return FALSE;
    }

    // Only attempt the call if the write is allowed (prevent sneaky side effects).

    AccessCheckResult destAccessCheck = destPolicyResult.CheckWriteAccess();
    if (destAccessCheck.ShouldDenyAccess()) 
    {
        DWORD denyError = destAccessCheck.DenialError();
        ReportIfNeeded(destAccessCheck, destinationOpContext, destPolicyResult, denyError);
        destAccessCheck.SetLastErrorToDenialError();
        return FALSE;
    }

    // Now we can safely try to hardlink, but note that the corresponding read of the source file may end up disallowed
    // (maybe the source file exists, as CreateHardLink requires, but we only allow non-existence probes).
    // Recall that failure of CreateHardLink is orthogonal to access-check failure.

    DWORD error = ERROR_SUCCESS;

    BOOL result = Real_CreateHardLinkW(
        lpFileName,
        lpExistingFileName,
        lpSecurityAttributes);

    if (!result) 
    {
        error = GetLastError();
    }
    
    FileReadContext sourceReadContext;
    sourceReadContext.OpenedDirectory = false; // TODO: Perhaps CreateHardLink fails with a nice error code in this case.
    sourceReadContext.InferExistenceFromError(error);

    AccessCheckResult sourceAccessCheck = sourcePolicyResult.CheckReadAccess(RequestedReadAccess::Read, sourceReadContext);

    ReportIfNeeded(sourceAccessCheck, sourceOpContext, sourcePolicyResult, error);
    ReportIfNeeded(destAccessCheck, destinationOpContext, destPolicyResult, error);

    if (sourceAccessCheck.ShouldDenyAccess()) 
    {
        result = FALSE;
        error = sourceAccessCheck.DenialError();
    }
    SetLastError(error);

    return result;
}

IMPLEMENTED(Detoured_CreateHardLinkA)
BOOL WINAPI Detoured_CreateHardLinkA(
    _In_       LPCSTR                lpFileName,
    _In_       LPCSTR                lpExistingFileName,
    __reserved LPSECURITY_ATTRIBUTES lpSecurityAttributes
    )
{
    {
        DetouredScope scope;
        if (scope.Detoured_IsDisabled() || IsNullOrEmptyA(lpFileName) || IsNullOrEmptyA(lpExistingFileName))
        {
            return Real_CreateHardLinkA(
                lpFileName,
                lpExistingFileName,
                lpSecurityAttributes);
        }
    }

    UnicodeConverter fileName(lpFileName);
    UnicodeConverter existingFileName(lpExistingFileName);
    return Detoured_CreateHardLinkW(
        fileName,
        existingFileName,
        lpSecurityAttributes);
}

IMPLEMENTED(Detoured_CreateSymbolicLinkW)
BOOLEAN WINAPI Detoured_CreateSymbolicLinkW(
    _In_ LPCWSTR lpSymlinkFileName,
    _In_ LPCWSTR lpTargetFileName,
    _In_ DWORD   dwFlags)
{
    DetouredScope scope;
    if (scope.Detoured_IsDisabled() ||
        IgnoreReparsePoints() ||
        IsNullOrEmptyW(lpSymlinkFileName) ||
        IsNullOrEmptyW(lpTargetFileName) ||
        IsSpecialDeviceName(lpSymlinkFileName) ||
        IsSpecialDeviceName(lpTargetFileName))
    {
        return Real_CreateSymbolicLinkW(
            lpSymlinkFileName,
            lpTargetFileName,
            dwFlags);
    }

    DWORD lastError = GetLastError();

    // Check to see if we can write at the symlink location.
    FileOperationContext opContextSrc = FileOperationContext(
        L"CreateSymbolicLink_Source",
        GENERIC_WRITE,
        0,
        CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL,
        lpSymlinkFileName);

    PolicyResult policyResultSrc;
    if (!policyResultSrc.Initialize(lpSymlinkFileName))
    {
        policyResultSrc.ReportIndeterminatePolicyAndSetLastError(opContextSrc);
        return FALSE;
    }

    // Check for write access on the symlink.
    AccessCheckResult accessCheckSrc = policyResultSrc.CheckWriteAccess();
    accessCheckSrc = AccessCheckResult::Combine(accessCheckSrc, policyResultSrc.CheckSymlinkCreationAccess());

    if (accessCheckSrc.ShouldDenyAccess())
    {
        lastError = accessCheckSrc.DenialError();
        ReportIfNeeded(accessCheckSrc, opContextSrc, policyResultSrc, lastError);
        SetLastError(lastError);
        return FALSE;
    }

    DWORD error = ERROR_SUCCESS;
    BOOLEAN result = Real_CreateSymbolicLinkW(
        lpSymlinkFileName,
        lpTargetFileName,
        dwFlags);

    error = GetLastError();

    ReportIfNeeded(accessCheckSrc, opContextSrc, policyResultSrc, error);

    SetLastError(error);
    return result;
}

IMPLEMENTED(Detoured_CreateSymbolicLinkA)
BOOLEAN WINAPI Detoured_CreateSymbolicLinkA(
    _In_ LPCSTR lpSymlinkFileName,
    _In_ LPCSTR lpTargetFileName,
    _In_ DWORD  dwFlags)
{
    {
        DetouredScope scope;
        if (scope.Detoured_IsDisabled() || IsNullOrEmptyA(lpSymlinkFileName) || IsNullOrEmptyA(lpTargetFileName))
        {
            return Real_CreateSymbolicLinkA(
                lpSymlinkFileName,
                lpTargetFileName,
                dwFlags);
        }
    }

    UnicodeConverter symlinkFileName(lpSymlinkFileName);
    UnicodeConverter targetFileName(lpTargetFileName);
    return Detoured_CreateSymbolicLinkW(
        symlinkFileName,
        targetFileName,
        dwFlags);
}

IMPLEMENTED(Detoured_FindFirstFileW)
HANDLE WINAPI Detoured_FindFirstFileW(
    _In_  LPCWSTR            lpFileName,
    _Out_ LPWIN32_FIND_DATAW lpFindFileData)
{
    // FindFirstFileExW is a strict superset. This line is essentially the same as the FindFirstFileW thunk in \minkernel\kernelbase\filefind.c
    return Detoured_FindFirstFileExW(lpFileName, FindExInfoStandard, lpFindFileData, FindExSearchNameMatch, NULL, 0);
}

IMPLEMENTED(Detoured_FindFirstFileA)
HANDLE WINAPI Detoured_FindFirstFileA(
    _In_   LPCSTR             lpFileName,
    _Out_  LPWIN32_FIND_DATAA lpFindFileData)
{
    // TODO:replace with Detoured_FindFirstFileW below
    return Real_FindFirstFileA(
        lpFileName,
        lpFindFileData);

    // TODO: Note that we can't simply forward to FindFirstFileW here after a unicode conversion.
    // The output value differs too - WIN32_FIND_DATA{A, W}
}

IMPLEMENTED(Detoured_FindFirstFileExW)
HANDLE WINAPI Detoured_FindFirstFileExW(
    _In_       LPCWSTR            lpFileName,
    _In_       FINDEX_INFO_LEVELS fInfoLevelId,
    _Out_      LPVOID             lpFindFileData,
    _In_       FINDEX_SEARCH_OPS  fSearchOp,
    __reserved LPVOID             lpSearchFilter,
    _In_       DWORD              dwAdditionalFlags)
{
    DetouredScope scope;
    if (scope.Detoured_IsDisabled() || IsNullOrEmptyW(lpFileName) || lpFindFileData == NULL ||
        lpSearchFilter != NULL ||
        (fInfoLevelId != FindExInfoStandard && fInfoLevelId != FindExInfoBasic) ||
        IsSpecialDeviceName(lpFileName)) 
    {
        return Real_FindFirstFileExW(lpFileName, fInfoLevelId, lpFindFileData, fSearchOp, lpSearchFilter, dwAdditionalFlags);
    }

    FileOperationContext fileOperationContext = FileOperationContext::CreateForRead(L"FindFirstFileEx", lpFileName);

    // Both of the currently understood info levels return WIN32_FIND_DATAW.
    LPWIN32_FIND_DATAW findFileDataAtLevel = (LPWIN32_FIND_DATAW)lpFindFileData;

    // There are two categories of FindFirstFile invocation that we can model differently:
    // - Probe: FindFirstFile("C:\componentA\componentB") where componentB is a normal path component.
    //          We model this as a normal probe to the full path. If FindFirstFile returns ERROR_FILE_NOT_FOUND, this is a normal anti-dependency.
    // - Enumeration: FindFirstFile("C:\componentA\wildcard") where the last component is a wildcard, e.g. "*cpp" or "*".
    //          We model this as (filtered) directory enumeration. This access is to C:\componentA, with imaginary anti-dependencies on everything
    //          that _could_ match the filter. This call starts enumerating, but also might return the first match to the wildcard (which requires its own access check).
    //          TODO: We currently cannot report or model invalidation of enumeration 'anti-dependencies', but can report what files are actually found.
    CanonicalizedPath canonicalizedPathIncludingFilter = CanonicalizedPath::Canonicalize(lpFileName);
    if (canonicalizedPathIncludingFilter.IsNull()) 
    {
        // TODO: This really shouldn't have failure cases. Maybe just failfast on allocation failure, etc.
        Dbg(L"FindFirstFileEx: Failed to canonicalize the search path; passing through.");
        return Real_FindFirstFileExW(lpFileName, fInfoLevelId, lpFindFileData, fSearchOp, lpSearchFilter, dwAdditionalFlags);
    }

    // First, get the policy for the directory itself; this entails removing the last component.
    PolicyResult directoryPolicyResult;
    directoryPolicyResult.Initialize(canonicalizedPathIncludingFilter.RemoveLastComponent());

    DWORD error = ERROR_SUCCESS;
    HANDLE searchHandle = Real_FindFirstFileExW(lpFileName, fInfoLevelId, lpFindFileData, fSearchOp, lpSearchFilter, dwAdditionalFlags);
    error = GetLastError();

    // Note that we check success via the returned handle. This function does not call SetLastError(ERROR_SUCCESS) on success. We stash
    // and restore the error code anyway so as to not perturb things.
    bool success = searchHandle != INVALID_HANDLE_VALUE;

    // ERROR_DIRECTORY means we had an lpFileName like X:\a\b where X:\a is a file rather than a directory.
    // In other words, this access is equivalent to a non-enumerating probe on a file X:\a.
    bool searchPathIsFile = error == ERROR_DIRECTORY;
	wchar_t const* filter = canonicalizedPathIncludingFilter.GetLastComponent();
    bool isEnumeration = !searchPathIsFile && PathContainsWildcard(filter);
    bool isProbeOfLastComponent = !isEnumeration && !searchPathIsFile;

    // Read context used for access-checking a probe to the search-directory.
    // This is only used if searchPathIsFile, i.e., we got ERROR_DIRECTORY.
    FileReadContext directoryProbeContext;
    directoryProbeContext.FileExistence = FileExistence::Existent;
    directoryProbeContext.OpenedDirectory = !searchPathIsFile;

    // Only report the enumeration if specified by the policy
    bool reportDirectoryEnumeration = directoryPolicyResult.ReportDirectoryEnumeration();
    bool explicitlyReportDirectoryEnumeration =  isEnumeration && reportDirectoryEnumeration;

    // TODO: Perhaps should have a specific access check for enumeration.
    //       For now, we always allow enumeration and report it.
    //       Since enumeration has historically not been understood or reported at all, this is a fine incremental move -
    //       given a policy flag for allowing enumeration, we'd apply it globally anyway.
    // TODO: Should include the wildcard in enumeration reports, so that directory enumeration assertions can be more precise.

    AccessCheckResult directoryAccessCheck = searchPathIsFile
        ? directoryPolicyResult.CheckReadAccess(RequestedReadAccess::Probe, directoryProbeContext) // Given X:\d\* we're probing X:\d (a file) 
        : AccessCheckResult( // Given X:\d\* we're enumerating X:\d (may or may not exist).
            isEnumeration ? RequestedAccess::Enumerate : RequestedAccess::Probe,
            ResultAction::Allow,
            explicitlyReportDirectoryEnumeration ? ReportLevel::ReportExplicit : ReportLevel::Ignore);

    if (!searchPathIsFile && !explicitlyReportDirectoryEnumeration && ReportAnyAccess(false))
    {
        // Ensure access is reported (not explicit) when report all accesses is specified
        directoryAccessCheck.ReportLevel = ReportLevel::Report;
    }

    // Now, we can establish a policy for the file actually found.
    // - If enumerating, we can only do this on success (some file actually found) - if the wildcard matches nothing, we can't invent a name for which to report an antidependency.
    //   TODO: This is okay, but we need to complement this behavior with reporting the enumeration on the directory.
    // - If probing, we can do this even on failure. If nothing is found, we have a simple anti-dependency on the fully-canonicalized path.
    PolicyResult filePolicyResult;
    bool canReportPreciseFileAccess;
    if (success && isEnumeration) 
    {
        assert(!searchPathIsFile);
        // Start enumeration: append the found name to get a sub-policy for the first file found.
        wchar_t const* enumeratedComponent = &findFileDataAtLevel->cFileName[0];
        filePolicyResult = directoryPolicyResult.GetPolicyForSubpath(enumeratedComponent);
        canReportPreciseFileAccess = true;
    }
    else if (isProbeOfLastComponent) 
    {
        assert(!searchPathIsFile);
        // Probe: success doesn't matter; append the last component to get a sub-policy (we excluded it before to get the directory policy).
        filePolicyResult = directoryPolicyResult.GetPolicyForSubpath(canonicalizedPathIncludingFilter.GetLastComponent());
        canReportPreciseFileAccess = true;
    }
    else 
    {
        // One of:
        // a) Enumerated an empty directory with a wildcard (!success)
        // b) Search-path is actually a file (searchPathIsFile)
        // In either case we don't have a concrete path for the final component and so can only report the directory access.
        canReportPreciseFileAccess = false;
    }

    // For the enumeration itself, we report ERROR_SUCCESS in the case that no matches were found (the directory itself exists).
    // FindFirstFileEx indicates no matches with ERROR_FILE_NOT_FOUND.
    DWORD enumerationError = (success || error == ERROR_FILE_NOT_FOUND) ? ERROR_SUCCESS : error;
    ReportIfNeeded(directoryAccessCheck, fileOperationContext, directoryPolicyResult, success ? ERROR_SUCCESS : enumerationError, -1, filter);

    // No need to enforce chain of reparse point accesses because if path is a symbolic link, the WIN32_FIND_DATA buffer already
    // contains information about the symbolic link, and not the target.

    // TODO: Respect ShouldDenyAccess for directoryAccessCheck.

    if (canReportPreciseFileAccess) 
    {
        assert(!filePolicyResult.IsIndeterminate());

        FileReadContext readContext;
        readContext.InferExistenceFromError(success ? ERROR_SUCCESS : error);
        readContext.OpenedDirectory = success && (readContext.FileExistence == FileExistence::Existent) && ((findFileDataAtLevel->dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0);

        AccessCheckResult fileAccessCheck = filePolicyResult.CheckReadAccess(
            isEnumeration ? RequestedReadAccess::EnumerationProbe : RequestedReadAccess::Probe,
            readContext);

        ReportIfNeeded(fileAccessCheck, fileOperationContext, filePolicyResult, success ? ERROR_SUCCESS : error);

        if (fileAccessCheck.ShouldDenyAccess()) 
        {
            // Note that we won't hard-deny enumeration probes (isEnumeration == true, requested EnumerationProbe). See CheckReadAccess.
            error = fileAccessCheck.DenialError();

            if (searchHandle != INVALID_HANDLE_VALUE) 
            {
                FindClose(searchHandle);
                searchHandle = INVALID_HANDLE_VALUE;
            }

            // Translate directory for debugging only.
            wstring debugOutFile;
            TranslateFilePath(wstring(canonicalizedPathIncludingFilter.RemoveLastComponent().GetPathString()), debugOutFile, true);
        }
        else if (success && isEnumeration) 
        {
            // We are returning a find handle that might return more results; mark it so that we can respond to FindNextFile on it.
            RegisterHandleOverlay(searchHandle, directoryAccessCheck, directoryPolicyResult, HandleType::Find);
        }

        if (success && filePolicyResult.ShouldOverrideTimestamps(fileAccessCheck)) 
        {
#if SUPER_VERBOSE
            Dbg(L"FindFirstFileExW: Overriding timestamps for %s", filePolicyResult.GetCanonicalizedPath().GetPathString());
#endif // SUPER_VERBOSE
            OverrideTimestampsForInputFile(findFileDataAtLevel);
        }

        // FindFirstFile is the most common way to determine short-names for files and directories (observed to be called by even GetShortPathName).
        // We want to hide short file names, since they are not deterministic, not always present, and we don't canonicalize them for enforcement.
        if (success) 
        {
            ScrubShortFileName(findFileDataAtLevel);
        }
    }

    SetLastError(error);
    return searchHandle;
}

IMPLEMENTED(Detoured_FindFirstFileExA)
HANDLE WINAPI Detoured_FindFirstFileExA(
    _In_       LPCSTR             lpFileName,
    _In_       FINDEX_INFO_LEVELS fInfoLevelId,
    _Out_      LPVOID             lpFindFileData,
    _In_       FINDEX_SEARCH_OPS  fSearchOp,
    __reserved LPVOID             lpSearchFilter,
    _In_       DWORD              dwAdditionalFlags)
{
    // TODO: Note that we can't simply forward to FindFirstFileW here after a unicode conversion.
    // The output value differs too - WIN32_FIND_DATA{A, W}

    return Real_FindFirstFileExA(
        lpFileName,
        fInfoLevelId,
        lpFindFileData,
        fSearchOp,
        lpSearchFilter,
        dwAdditionalFlags);
}

IMPLEMENTED(Detoured_FindNextFileW)
BOOL WINAPI Detoured_FindNextFileW(
    _In_  HANDLE             hFindFile,
    _Out_ LPWIN32_FIND_DATAW lpFindFileData)
{
    DetouredScope scope;
    DWORD error = ERROR_SUCCESS; 
    BOOL result = Real_FindNextFileW(hFindFile, lpFindFileData);
    error = GetLastError();

    if (scope.Detoured_IsDisabled() || IsNullOrInvalidHandle(hFindFile) || lpFindFileData == nullptr)
    {
        return result;
    }

    if (!result) 
    {
        // TODO: This is likely ERROR_NO_MORE_FILES; is there anything more to check or report when enumeration ends?
        return result;
    }

    HandleOverlayRef overlay = TryLookupHandleOverlay(hFindFile);
    if (overlay != nullptr) 
    {
        FileOperationContext fileOperationContext = FileOperationContext::CreateForRead(L"FindNextFile", overlay->Policy.GetCanonicalizedPath().GetPathString());
        
        wchar_t const* enumeratedComponent = &lpFindFileData->cFileName[0];
        PolicyResult filePolicyResult = overlay->Policy.GetPolicyForSubpath(enumeratedComponent);

        FileReadContext readContext;
        readContext.FileExistence = FileExistence::Existent;
        readContext.OpenedDirectory = (lpFindFileData->dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;

        AccessCheckResult accessCheck = filePolicyResult.CheckReadAccess(RequestedReadAccess::EnumerationProbe, readContext);
        ReportIfNeeded(accessCheck, fileOperationContext, filePolicyResult, result ? ERROR_SUCCESS : error);

        // No need to enforce chain of reparse point accesses because if path is a symbolic link, the WIN32_FIND_DATA buffer already
        // contains information about the symbolic link, and not the target.

        if (filePolicyResult.ShouldOverrideTimestamps(accessCheck)) 
        {
#if SUPER_VERBOSE
            Dbg(L"FindNextFile: Overriding timestamps for %s", filePolicyResult.GetCanonicalizedPath().GetPathString());
#endif // SUPER_VERBOSE
            OverrideTimestampsForInputFile(lpFindFileData);
        }

        // See usage in FindFirstFileExW
        ScrubShortFileName(lpFindFileData);

        // N.B. We do not check ShouldDenyAccess here. It is unusual for FindNextFile to fail. Would the caller clean up the find handle? Etc.
        //      Conveniently, for historical reasons, enumeration-based probes (RequestedReadAccess::EnumerationProbe) always have !ShouldDenyAccess() anyway - see CheckReadAccess.
    }
    else 
    {
#if SUPER_VERBOSE
        Dbg(L"FindNextFile: Failed to find a handle overlay for policy information; conservatively not overriding timestamps");
#endif // SUPER_VERBOSE
    }

    SetLastError(error);
    return result;
}

IMPLEMENTED(Detoured_FindNextFileA)
BOOL WINAPI Detoured_FindNextFileA(
    _In_  HANDLE             hFindFile,
    _Out_ LPWIN32_FIND_DATAA lpFindFileData)
{
    // TODO:replace with the same logic as Detoured_FindNextFileW
    // Note that we can't simply forward to FindFirstFileW here after a unicode conversion.
    // The output value differs too - WIN32_FIND_DATA{A, W}
    return Real_FindNextFileA(
        hFindFile,
        lpFindFileData);
}

IMPLEMENTED(Detoured_GetFileInformationByHandleEx)
BOOL WINAPI Detoured_GetFileInformationByHandleEx(
    _In_  HANDLE                    hFile,
    _In_  FILE_INFO_BY_HANDLE_CLASS fileInformationClass,
    _Out_ LPVOID                    lpFileInformation,
    _In_  DWORD                     dwBufferSize)
{
    DetouredScope scope;

    DWORD error = ERROR_SUCCESS;
    BOOL result = Real_GetFileInformationByHandleEx(
            hFile,
            fileInformationClass,
            lpFileInformation,
            dwBufferSize);

    error = GetLastError();

    if (scope.Detoured_IsDisabled() || IsNullOrInvalidHandle(hFile) || fileInformationClass != FileBasicInfo || lpFileInformation == nullptr)
    {
        return result;
    }

    assert(fileInformationClass == FileBasicInfo);
    FILE_BASIC_INFO* fileBasicInfo = (FILE_BASIC_INFO*)lpFileInformation;

    HandleOverlayRef overlay = TryLookupHandleOverlay(hFile);
    if (overlay != nullptr) 
    {
        if (overlay->Policy.ShouldOverrideTimestamps(overlay->AccessCheck)) 
        {
#if SUPER_VERBOSE
            Dbg(L"GetFileInformationByHandleEx: Overriding timestamps for %s", overlay->Policy.GetCanonicalizedPath().GetPathString());
#endif // SUPER_VERBOSE
            OverrideTimestampsForInputFile(fileBasicInfo);
        }
    }
    else 
    {
#if SUPER_VERBOSE
        Dbg(L"GetFileInformationByHandleEx: Failed to find a handle overlay for policy information; conservatively not overriding timestamps");
#endif // SUPER_VERBOSE
    }

    SetLastError(error);
    return result;
}

IMPLEMENTED(Detoured_FindClose)
BOOL WINAPI Detoured_FindClose(_In_ HANDLE handle)
{
    DetouredScope scope;

    DWORD error = ERROR_SUCCESS;

    // Make sure the handle is closed after the object is removed from the map.
    // This way the handle will never be assigned to a another object before removed from the table.
    CloseHandleOverlay(handle, true);

    BOOL result = Real_FindClose(handle);
    error = GetLastError();

    if (scope.Detoured_IsDisabled() || IsNullOrInvalidHandle(handle))
    {
        return result;
    }

    SetLastError(error);
    return result;
}

IMPLEMENTED(Detoured_GetFileInformationByHandle)
BOOL WINAPI Detoured_GetFileInformationByHandle(
    _In_  HANDLE                       hFile,
    _Out_ LPBY_HANDLE_FILE_INFORMATION lpFileInformation)
{
    DetouredScope scope;

    DWORD error = ERROR_SUCCESS;
    BOOL result = Real_GetFileInformationByHandle(hFile, lpFileInformation);
    error = GetLastError();

    if (scope.Detoured_IsDisabled() || IsNullOrInvalidHandle(hFile) || lpFileInformation == nullptr)
    {
        return result;
    }

    HandleOverlayRef overlay = TryLookupHandleOverlay(hFile);
    if (overlay != nullptr) 
    {
        if (overlay->Policy.ShouldOverrideTimestamps(overlay->AccessCheck)) 
        {
#if SUPER_VERBOSE
            Dbg(L"GetFileInformationByHandle: Overriding timestamps for %s", overlay->Policy.GetCanonicalizedPath().GetPathString());
#endif // SUPER_VERBOSE
            OverrideTimestampsForInputFile(lpFileInformation);
        }
    }
    else 
    {
#if SUPER_VERBOSE
        Dbg(L"GetFileInformationByHandle: Failed to find a handle overlay for policy information; conservatively not overriding timestamps");
#endif // SUPER_VERBOSE
    }

    SetLastError(error);
    return result;
}

static BOOL DeleteUsingSetFileInformationByHandle(
    _In_ HANDLE                    hFile,
    _In_ FILE_INFO_BY_HANDLE_CLASS FileInformationClass,
    _In_ LPVOID                    lpFileInformation,
    _In_ DWORD                     dwBufferSize,
    _In_ const wstring&       fullPath)
{
    FileOperationContext sourceOpContext = FileOperationContext(
        L"SetFileInformationByHandle_Source",
        DELETE,
        0,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        fullPath.c_str());

    PolicyResult sourcePolicyResult;

    if (!sourcePolicyResult.Initialize(fullPath.c_str()))
    {
        sourcePolicyResult.ReportIndeterminatePolicyAndSetLastError(sourceOpContext);
        return FALSE;
    }

    AccessCheckResult sourceAccessCheck = sourcePolicyResult.CheckWriteAccess();

    if (sourceAccessCheck.ShouldDenyAccess())
    {
        DWORD denyError = sourceAccessCheck.DenialError();
        ReportIfNeeded(sourceAccessCheck, sourceOpContext, sourcePolicyResult, denyError);
        sourceAccessCheck.SetLastErrorToDenialError();
        return FALSE;
    }

    DWORD error = ERROR_SUCCESS;

    BOOL result = Real_SetFileInformationByHandle(
        hFile,
        FileInformationClass,
        lpFileInformation,
        dwBufferSize);

    if (!result) 
    {
        error = GetLastError();
    }

    ReportIfNeeded(sourceAccessCheck, sourceOpContext, sourcePolicyResult, error);

    SetLastError(error);

    return result;
}

static BOOL RenameUsingSetFileInformationByHandle(
    _In_ HANDLE                    hFile,
    _In_ FILE_INFO_BY_HANDLE_CLASS FileInformationClass,
    _In_ LPVOID                    lpFileInformation,
    _In_ DWORD                     dwBufferSize,
    _In_ const wstring&            fullPath)
{
    FileOperationContext sourceOpContext = FileOperationContext(
        L"SetFileInformationByHandle_Source",
        DELETE,
        0,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        fullPath.c_str());

    PolicyResult sourcePolicyResult;

    if (!sourcePolicyResult.Initialize(fullPath.c_str()))
    {
        sourcePolicyResult.ReportIndeterminatePolicyAndSetLastError(sourceOpContext);
        return FALSE;
    }

    AccessCheckResult sourceAccessCheck = sourcePolicyResult.CheckWriteAccess();

    if (sourceAccessCheck.ShouldDenyAccess())
    {
        DWORD denyError = sourceAccessCheck.DenialError();
        ReportIfNeeded(sourceAccessCheck, sourceOpContext, sourcePolicyResult, denyError);
        sourceAccessCheck.SetLastErrorToDenialError();
        return FALSE;
    }

    wstring targetFileName;

    DWORD lastError = GetLastError();

    PFILE_RENAME_INFORMATION pRenameInfo = (PFILE_RENAME_INFORMATION)lpFileInformation;

    if (!TryGetFileNameFromFileInformation(
        pRenameInfo->FileName,
        pRenameInfo->FileNameLength,
        pRenameInfo->RootDirectory,
        targetFileName)
        || targetFileName.empty())
    {
        SetLastError(lastError);

        return Real_SetFileInformationByHandle(
            hFile,
            FileInformationClass,
            lpFileInformation,
            dwBufferSize);
    }

    // Contrary to the documentation, pRenameInfo->RootDirectory for renaming using SetFileInformationByHandle
    // should always be NULL.

    FileOperationContext destinationOpContext = FileOperationContext(
        L"SetFileInformationByHandle_Dest",
        GENERIC_WRITE,
        0,
        CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL,
        targetFileName.c_str());

    PolicyResult destPolicyResult;

    if (!destPolicyResult.Initialize(targetFileName.c_str()))
    {
        destPolicyResult.ReportIndeterminatePolicyAndSetLastError(destinationOpContext);
        return FALSE;
    }

    AccessCheckResult destAccessCheck = destPolicyResult.CheckWriteAccess();

    if (destAccessCheck.ShouldDenyAccess())
    {
        // We report the destination access here since we are returning early. Otherwise it is deferred until post-read.
        DWORD denyError = destAccessCheck.DenialError();
        ReportIfNeeded(destAccessCheck, destinationOpContext, destPolicyResult, denyError);
        destAccessCheck.SetLastErrorToDenialError();
        return FALSE;
    }

    bool isHandleOfDirectory;
    bool renameDirectory = false;
    vector<ReportData> filesAndDirectoriesToReport;

    if (TryCheckHandleOfDirectory(hFile, true, isHandleOfDirectory) && isHandleOfDirectory)
    {
        renameDirectory = true;

        if (!ValidateMoveDirectory(
                L"SetFileInformationByHandle_Source", 
                L"SetFileInformationByHandle_Dest",
                fullPath.c_str(), 
                targetFileName.c_str(), 
                filesAndDirectoriesToReport))
        {
            return FALSE;
        }
    }

    DWORD error = ERROR_SUCCESS;

    BOOL result = Real_SetFileInformationByHandle(
        hFile,
        FileInformationClass,
        lpFileInformation,
        dwBufferSize);

    if (!result)
    {
        error = GetLastError();
    }

    ReportIfNeeded(sourceAccessCheck, sourceOpContext, sourcePolicyResult, error);
    ReportIfNeeded(destAccessCheck, destinationOpContext, destPolicyResult, error);

    if (renameDirectory)
    {
        for (vector<ReportData>::const_iterator it = filesAndDirectoriesToReport.cbegin(); it != filesAndDirectoriesToReport.cend(); ++it)
        {
            ReportIfNeeded(it->GetAccessCheckResult(), it->GetFileOperationContext(), it->GetPolicyResult(), error);
        }
    }

    SetLastError(error);

    return result;
}

IMPLEMENTED(Detoured_SetFileInformationByHandle)
BOOL WINAPI Detoured_SetFileInformationByHandle(
    _In_ HANDLE                    hFile,
    _In_ FILE_INFO_BY_HANDLE_CLASS FileInformationClass,
    _In_ LPVOID                    lpFileInformation,
    _In_ DWORD                     dwBufferSize)
{
    bool isDisposition =
        FileInformationClass == FILE_INFO_BY_HANDLE_CLASS::FileDispositionInfo
        || FileInformationClass == FILE_INFO_BY_HANDLE_CLASS::FileDispositionInfoEx;

    bool isRename =
        FileInformationClass == FILE_INFO_BY_HANDLE_CLASS::FileRenameInfo
        || FileInformationClass == FILE_INFO_BY_HANDLE_CLASS::FileRenameInfoEx;

    if ((!isDisposition && !isRename) || IgnoreSetFileInformationByHandle()) 
    {

        // We ignore the use of SetFileInformationByHandle when it is not file renaming or file deletion. 
        // However, since SetInformationByHandle may call other APIs, and those APIs may be detoured,
        // we don't check for DetouredScope yet.
        return Real_SetFileInformationByHandle(
            hFile,
            FileInformationClass,
            lpFileInformation,
            dwBufferSize);
    }

    DetouredScope scope;
    if (scope.Detoured_IsDisabled()) 
    {
        return Real_SetFileInformationByHandle(
            hFile,
            FileInformationClass,
            lpFileInformation,
            dwBufferSize);
    }

    if (isDisposition) 
    {
        bool isDeletion = false;
        if (FileInformationClass == FILE_INFO_BY_HANDLE_CLASS::FileDispositionInfo) 
        {
            PFILE_DISPOSITION_INFO pDispStruct = (PFILE_DISPOSITION_INFO)lpFileInformation;
            if (pDispStruct->DeleteFile) 
            {
                isDeletion = true;
            }
        }
        else if (FileInformationClass == FILE_INFO_BY_HANDLE_CLASS::FileDispositionInfoEx) 
        {
            PFILE_DISPOSITION_INFO_EX pDispStructEx = (PFILE_DISPOSITION_INFO_EX)lpFileInformation;
            if ((pDispStructEx->Flags & FILE_DISPOSITION_FLAG_DELETE) != 0) 
            {
                isDeletion = true;
            }
        }

        if (!isDeletion) 
        {
            // Not a deletion, don't detour.
            return Real_SetFileInformationByHandle(
                hFile,
                FileInformationClass,
                lpFileInformation,
                dwBufferSize);
        }
    }

    DWORD lastError = GetLastError();

    wstring srcPath;

    DWORD getFinalPathByHandle = DetourGetFinalPathByHandle(hFile, srcPath);
    if ((getFinalPathByHandle != ERROR_SUCCESS) || IsSpecialDeviceName(srcPath.c_str()) || IsNullOrEmptyW(srcPath.c_str()))
    {
        if (getFinalPathByHandle != ERROR_SUCCESS) 
        {
            Dbg(L"Detoured_SetFileInformationByHandle: DetourGetFinalPathByHandle: %d", getFinalPathByHandle);
        }

        SetLastError(lastError);

        return Real_SetFileInformationByHandle(
            hFile,
            FileInformationClass,
            lpFileInformation,
            dwBufferSize);
    }

    return isDisposition
        ? DeleteUsingSetFileInformationByHandle(
            hFile,
            FileInformationClass,
            lpFileInformation,
            dwBufferSize,
            srcPath)
        : RenameUsingSetFileInformationByHandle(
            hFile,
            FileInformationClass,
            lpFileInformation,
            dwBufferSize,
            srcPath);
}

HANDLE WINAPI Detoured_OpenFileMappingW(
    _In_ DWORD   dwDesiredAccess,
    _In_ BOOL    bInheritHandle,
    _In_ LPCWSTR lpName)
{
    // TODO:implement detours logic
    return Real_OpenFileMappingW(
        dwDesiredAccess,
        bInheritHandle,
        lpName);
}

HANDLE WINAPI Detoured_OpenFileMappingA(
    _In_  DWORD  dwDesiredAccess,
    _In_  BOOL   bInheritHandle,
    _In_  LPCSTR lpName)
{
    {
        DetouredScope scope;
        if (scope.Detoured_IsDisabled() || IsNullOrEmptyA(lpName))
        {
            return Real_OpenFileMappingA(
                dwDesiredAccess,
                bInheritHandle,
                lpName);
        }
    }

    UnicodeConverter name(lpName);
    return Detoured_OpenFileMappingW(
        dwDesiredAccess,
        bInheritHandle,
        name);
}

// Detoured_GetTempFileNameW
//
// lpPathName is typically "." or result of GetTempPath (which doesn't need to be detoured, itself)
// lpPrefixString is allowed to be empty.
UINT WINAPI Detoured_GetTempFileNameW(
    _In_  LPCWSTR lpPathName,
    _In_  LPCWSTR lpPrefixString,
    _In_  UINT    uUnique,
    _Out_ LPTSTR  lpTempFileName)
{
    // TODO:implement detours logic
    return Real_GetTempFileNameW(
        lpPathName,
        lpPrefixString,
        uUnique,
        lpTempFileName);
}

UINT WINAPI Detoured_GetTempFileNameA(
    _In_  LPCSTR lpPathName,
    _In_  LPCSTR lpPrefixString,
    _In_  UINT   uUnique,
    _Out_ LPSTR  lpTempFileName)
{
    // TODO:implement detours logic
    return Real_GetTempFileNameA(
        lpPathName,
        lpPrefixString,
        uUnique,
        lpTempFileName);
}

/// <summary>
/// Performs a read-only probe of a path to simulate a read-only variant of CreateDirectory (if the target filename exists already, CreateDirectory
/// should act like a generic read probe; to be accurate we should check if the probe target exists or is a directory, etc). 
/// </summary>
/// <remarks>
/// If the read-only probe indicates that CreateDirectory would have attempted to write, instead writeAccessCheck is returned (requested access is Write).
/// Otherwise, a Probe-level access check is returned (which may or may not be permitted, based on policy).
///
/// In all, we want the treatement of CreateDirectory to be equivalent to the following separable accesses:
/// <code>
/// atomic {
///   if (Probe(path) == FinalComponentDoesNotExist) { Write() } else { fail }
/// }
/// </code>
/// (but we want to report one access, i.e., the Write if it happens otherwise the probe).
/// </remarks>
static AccessCheckResult CreateDirectorySafeProbe(
    AccessCheckResult writeAccessCheck, 
    FileOperationContext const& opContext, 
    PolicyResult const& policyResult, 
    DWORD* probeError) 
{
    DWORD attributes = GetFileAttributesW(opContext.NoncanonicalPath);

    *probeError = ERROR_SUCCESS;

    if (attributes == INVALID_FILE_ATTRIBUTES) 
    {
        *probeError = GetLastError();
    }
    
    FileReadContext probeContext;
    probeContext.OpenedDirectory = attributes != INVALID_FILE_ATTRIBUTES && ((attributes & FILE_ATTRIBUTE_DIRECTORY) != 0);
    probeContext.InferExistenceFromError(*probeError);

    // If we are checking all CreateDirectory calls, just reuse the writeAccessCheck we already have.
    // This will result in blocking CreateDirectory (i.e., returning ERROR_ACCESS_DENIED) if a directory already exists 
    // and writeAccessCheck.ResultAction == ResultAction::Deny.
    AccessCheckResult probeAccessCheck = DirectoryCreationAccessEnforcement()
        ? writeAccessCheck
        // otherwise, create a read-only probe
        : policyResult.CheckReadAccess(RequestedReadAccess::Probe, probeContext);
    
    if (probeContext.FileExistence == FileExistence::Existent) 
    {
        // See http://msdn.microsoft.com/en-us/library/windows/desktop/aa363855(v=vs.85).aspx
        *probeError = ERROR_ALREADY_EXISTS;
    }
    else if (*probeError == ERROR_FILE_NOT_FOUND) 
    {
        probeAccessCheck = AccessCheckResult::Combine(writeAccessCheck, AccessCheckResult::DenyOrWarn(RequestedAccess::Write));
        
        // We should set the last error to access denied only if the write access is denied. Otherwise the tool
        // will just create the directory.
        // If we set the error to DENY_ACCESS if the write is allowed, some Unix ported tools (like perl and Node)
        // are not checking the return value of the function, but the last error and fail with EPERM error.
        if (writeAccessCheck.ShouldDenyAccess())
        {
            // Final path component didn't exist, yet we didn't want to create it.
            *probeError = ERROR_ACCESS_DENIED;
        }

    } // Else, maybe ERROR_PATH_NOT_FOUND?

    if (probeAccessCheck.ShouldDenyAccess()) 
    {
        *probeError = probeAccessCheck.DenialError();
    }

    return probeAccessCheck;
}

// Detoured_CreateDirectoryW
//
// The value of lpSecurityAttributes is not important to our access policy,
// so we can ignore it when determining whether this call is successful.
IMPLEMENTED(Detoured_CreateDirectoryW)
BOOL WINAPI Detoured_CreateDirectoryW(
    _In_     LPCWSTR               lpPathName,
    _In_opt_ LPSECURITY_ATTRIBUTES lpSecurityAttributes)
{
    DetouredScope scope;
    if (scope.Detoured_IsDisabled() ||
        IsNullOrEmptyW(lpPathName) ||
        IsSpecialDeviceName(lpPathName))
    {
        return Real_CreateDirectoryW(
            lpPathName,
            lpSecurityAttributes);
    }

    FileOperationContext opContext(
        L"CreateDirectory",
        GENERIC_WRITE,
        0,
        OPEN_ALWAYS,
        FILE_ATTRIBUTE_DIRECTORY,
        lpPathName
        );

    PolicyResult policyResult;
    if (!policyResult.Initialize(lpPathName)) 
    {
        policyResult.ReportIndeterminatePolicyAndSetLastError(opContext);
        return FALSE;
    }

    AccessCheckResult accessCheck = policyResult.CheckCreateDirectoryAccess();

    if (accessCheck.ShouldDenyAccess()) 
    {
        // Oh no! We can't create the directory. Well, it turns out that there are tons of calls to CreateDirectory just to 'ensure' all path components exist,
        // and many times those directories already do exist (C:\users for example, or even an output directory for a tool). So, one last chance, perhaps we
        // can rephrase this as a probe.
        DWORD probeError;
        AccessCheckResult probeAccessCheck = CreateDirectorySafeProbe(accessCheck, opContext, policyResult, /*out*/ &probeError);
        ReportIfNeeded(probeAccessCheck, opContext, policyResult, probeError);
        SetLastError(probeError);
        return FALSE; // Still a kind of failure; didn't create a directory.
    }

    BOOL result = Real_CreateDirectoryW(
        lpPathName,
        lpSecurityAttributes);
    DWORD error = ERROR_SUCCESS;
    if (!result) 
    {
        error = GetLastError();
    }

    if (!result && accessCheck.ResultAction != ResultAction::Allow) 
    {
        // On error, we didn't create a directory, i.e., we did not write.
        // We retry as a read just like above; this ensures ResultAction::Warn acts like ResultAction::Deny.
        
        AccessCheckResult readAccessCheck = CreateDirectorySafeProbe(accessCheck, opContext, policyResult, /*out*/ &error);
        ReportIfNeeded(readAccessCheck, opContext, policyResult, error);
    }
    else 
    {
        ReportIfNeeded(accessCheck, opContext, policyResult, error);
    }

    SetLastError(error);
    return result;
}

BOOL WINAPI Detoured_CreateDirectoryA(
    _In_     LPCSTR                lpPathName,
    _In_opt_ LPSECURITY_ATTRIBUTES lpSecurityAttributes)
{
    {
        DetouredScope scope;
        if (scope.Detoured_IsDisabled() || IsNullOrEmptyA(lpPathName))
        {
            return Real_CreateDirectoryA(
                lpPathName,
                lpSecurityAttributes);
        }
    }

    UnicodeConverter pathName(lpPathName);
    return Detoured_CreateDirectoryW(
        pathName,
        lpSecurityAttributes);
}

BOOL WINAPI Detoured_CreateDirectoryExW(
    _In_     LPCWSTR               lpTemplateDirectory,
    _In_     LPCWSTR               lpNewDirectory,
    _In_opt_ LPSECURITY_ATTRIBUTES lpSecurityAttributes)
{
    // TODO:implement detours logic
    return Real_CreateDirectoryExW(
        lpTemplateDirectory,
        lpNewDirectory,
        lpSecurityAttributes);
}

BOOL WINAPI Detoured_CreateDirectoryExA(
    _In_     LPCSTR                lpTemplateDirectory,
    _In_     LPCSTR                lpNewDirectory,
    _In_opt_ LPSECURITY_ATTRIBUTES lpSecurityAttributes)
{
    {
        DetouredScope scope;
        if (scope.Detoured_IsDisabled() ||
            IsNullOrEmptyA(lpTemplateDirectory))
        {
            return Real_CreateDirectoryExA(
                lpTemplateDirectory,
                lpNewDirectory,
                lpSecurityAttributes);
        }
    }

    UnicodeConverter templateDir(lpTemplateDirectory);
    UnicodeConverter newDir(lpNewDirectory);
    return Detoured_CreateDirectoryExW(
        templateDir,
        newDir,
        lpSecurityAttributes);
}

IMPLEMENTED(Detoured_RemoveDirectoryW)
BOOL WINAPI Detoured_RemoveDirectoryW(_In_ LPCWSTR lpPathName)
{
    DetouredScope scope;
    if (scope.Detoured_IsDisabled() ||
        IsNullOrEmptyW(lpPathName) ||
        IsSpecialDeviceName(lpPathName))
    {
        return Real_RemoveDirectoryW(lpPathName);
    }

    FileOperationContext opContext(
        L"RemoveDirectory",
        DELETE,
        0,
        OPEN_ALWAYS,
        FILE_ATTRIBUTE_DIRECTORY,
        lpPathName
        );

    PolicyResult policyResult;
    if (!policyResult.Initialize(lpPathName)) 
    {
        policyResult.ReportIndeterminatePolicyAndSetLastError(opContext);
        return FALSE;
    }

    AccessCheckResult accessCheck = policyResult.CheckWriteAccess();

    if (accessCheck.ShouldDenyAccess()) 
    {
        DWORD denyError = accessCheck.DenialError();
        ReportIfNeeded(accessCheck, opContext, policyResult, denyError);
        accessCheck.SetLastErrorToDenialError();
        return FALSE;
    }

    BOOL result = Real_RemoveDirectoryW(lpPathName);
    DWORD error = ERROR_SUCCESS;
    if (!result) 
    {
        error = GetLastError();
    }

    ReportIfNeeded(accessCheck, opContext, policyResult, error);

    return result;
}

IMPLEMENTED(Detoured_RemoveDirectoryA)
BOOL WINAPI Detoured_RemoveDirectoryA(_In_ LPCSTR lpPathName)
{
    {
        DetouredScope scope;
        if (scope.Detoured_IsDisabled() || IsNullOrEmptyA(lpPathName))
        {
            return Real_RemoveDirectoryA(lpPathName);
        }
    }

    UnicodeConverter pathName(lpPathName);
    return Detoured_RemoveDirectoryW(pathName);
}

BOOL WINAPI Detoured_DecryptFileW(
    _In_       LPCWSTR lpFileName,
    __reserved DWORD dwReserved)
{
    // TODO:implement detours logic
    return Real_DecryptFileW(
        lpFileName,
        dwReserved);
}

BOOL WINAPI Detoured_DecryptFileA(
    _In_       LPCSTR lpFileName,
    __reserved DWORD dwReserved)
{
    {
        DetouredScope scope;
        if (scope.Detoured_IsDisabled() || IsNullOrEmptyA(lpFileName))
        {
            return Real_DecryptFileA(
                lpFileName,
                dwReserved);
        }
    }

    UnicodeConverter fileName(lpFileName);
    return Detoured_DecryptFileW(
        fileName,
        dwReserved);
}

BOOL WINAPI Detoured_EncryptFileW(_In_ LPCWSTR lpFileName)
{
    // TODO:implement detours logic
    return Real_EncryptFileW(lpFileName);
}

BOOL WINAPI Detoured_EncryptFileA(_In_ LPCSTR lpFileName)
{
    {
        DetouredScope scope;
        if (scope.Detoured_IsDisabled() || IsNullOrEmptyA(lpFileName))
        {
            return Real_EncryptFileA(lpFileName);
        }
    }

    UnicodeConverter fileName(lpFileName);
    return Detoured_EncryptFileW(fileName);
}

DWORD WINAPI Detoured_OpenEncryptedFileRawW(
    _In_  LPCWSTR lpFileName,
    _In_  ULONG   ulFlags,
    _Out_ PVOID*  pvContext)
{
    // TODO:implement detours logic
    return Real_OpenEncryptedFileRawW(
        lpFileName,
        ulFlags,
        pvContext);
}

DWORD WINAPI Detoured_OpenEncryptedFileRawA(
    _In_  LPCSTR lpFileName,
    _In_  ULONG  ulFlags,
    _Out_ PVOID* pvContext)
{
    {
        DetouredScope scope;
        if (scope.Detoured_IsDisabled() || IsNullOrEmptyA(lpFileName))
        {
            return Real_OpenEncryptedFileRawA(
                lpFileName,
                ulFlags,
                pvContext);
        }
    }

    UnicodeConverter fileName(lpFileName);
    return Detoured_OpenEncryptedFileRawW(
        fileName,
        ulFlags,
        pvContext);
}

// Detoured_OpenFileById
//
// hFile is needed to get access to the drive or volume. It doesn't matter what
//      file is requested, but it cannot be NULL or INVALID.
// lpFileID must not be null because it contains the ID of the file to open.
HANDLE WINAPI Detoured_OpenFileById(
    _In_     HANDLE                hFile,
    _In_     LPFILE_ID_DESCRIPTOR  lpFileID,
    _In_     DWORD                 dwDesiredAccess,
    _In_     DWORD                 dwShareMode,
    _In_opt_ LPSECURITY_ATTRIBUTES lpSecurityAttributes,
    _In_     DWORD                 dwFlags)
{
    // TODO:implement detours logic
    return Real_OpenFileById(
        hFile,
        lpFileID,
        dwDesiredAccess,
        dwShareMode,
        lpSecurityAttributes,
        dwFlags);
}

IMPLEMENTED(Detoured_GetFinalPathNameByHandleA)
DWORD WINAPI Detoured_GetFinalPathNameByHandleA(
    _In_  HANDLE hFile,
    _Out_ LPSTR lpszFilePath,
    _In_  DWORD cchFilePath,
    _In_  DWORD dwFlags)
{
    unique_ptr<wchar_t[]> wideFilePathBuffer(new wchar_t[cchFilePath]);
    DWORD err = Detoured_GetFinalPathNameByHandleW(hFile, wideFilePathBuffer.get(), cchFilePath, dwFlags);

    if (err == 0) 
    {
        return GetLastError();
    }

    if (err > cchFilePath) 
    {
        return err;
    }

    int numCharsRequired = WideCharToMultiByte(CP_ACP, 0, wideFilePathBuffer.get(), -1, NULL, 0, NULL, NULL);

    if ((unsigned)numCharsRequired < cchFilePath) 
    {
        int error = 0;

        // We do here -1, because:
        // Docs of WideCharToMultiByte:
        // Under remarks : "To null-terminate an output string for this function, the application should pass in -1 or explicitly count the terminating null character for the input string."
        // It is passed - 1 for the input string length, which implies the routine will null terminate the output.
        // Under return value : "Returns the number of bytes written to the buffer pointed to by lpMultiByteStr if successful."
        // Since the routine wrote the null terminator, the bytes written return value should include it.
        // Docs for GetFinalPathNameByHandle:
        // Section Return Value
        // If the function succeeds, the return value is the length of the string received by lpszFilePath, in TCHARs. This value does not include the size of the terminating null character.
        if ((unsigned)(numCharsRequired - 1) == cchFilePath)
        {
            // We need a new buffer to get the multibyte string. It will include space for the \0 charachter.
            int extraCharBuffLen = (int)cchFilePath + 1;
            unique_ptr<char[]> extraCharFilePathBuffer(new char[(size_t)extraCharBuffLen]);
            error = WideCharToMultiByte(CP_ACP, 0, wideFilePathBuffer.get(), -1, extraCharFilePathBuffer.get(), extraCharBuffLen, NULL, NULL);
            if (error != 0)
            {
                strncpy_s(lpszFilePath, cchFilePath, extraCharFilePathBuffer.get(), cchFilePath);
            }
        }
        else
        {
            error = WideCharToMultiByte(CP_ACP, 0, wideFilePathBuffer.get(), -1, lpszFilePath, (int)cchFilePath, NULL, NULL);
        }

        if (error == 0)
        {
            return (DWORD)error;
        }
    }

    // Substract -1 since the \0 char is included.
    return (DWORD) numCharsRequired - 1;
}

IMPLEMENTED(Detoured_GetFinalPathNameByHandleW)
DWORD WINAPI Detoured_GetFinalPathNameByHandleW(
    _In_  HANDLE hFile,
    _Out_ LPTSTR lpszFilePath,
    _In_  DWORD  cchFilePath,
    _In_  DWORD  dwFlags)
{
    DetouredScope scope;

    if (scope.Detoured_IsDisabled() || IgnoreGetFinalPathNameByHandle())
    {
        return Real_GetFinalPathNameByHandleW(hFile, lpszFilePath, cchFilePath, dwFlags);
    }

    DWORD err = Real_GetFinalPathNameByHandleW(hFile, lpszFilePath, cchFilePath, dwFlags);

    if (err == 0)
    {
        SetLastError(err);
    }
    else if (err < cchFilePath)
    {
        wstring normalizedPath;
        TranslateFilePath(wstring(lpszFilePath), normalizedPath, false);

        if (normalizedPath.length() <= cchFilePath)
        {
            wcscpy_s(lpszFilePath, cchFilePath, normalizedPath.c_str());
        }

        return (DWORD)normalizedPath.length();
    }

    return err;
}

// Detoured_NtQueryDirectoryFile
//
// FileHandle            - a handle for the file object that represents the directory for which information is being requested.
// Event                 - an optional handle for a caller-created event.
// ApcRoutine            - an address of an optional, caller-supplied APC routine to be called when the requested operation completes.
// ApcContext            - an optional pointer to a caller-determined context area to be passed to APC routine, if one was specified,
//                         or to be posted to the associated I / O completion object.
// IoStatusBlock         - A pointer to an IO_STATUS_BLOCK structure that receives the final completion status and information about the operation.
// FileInformation       - A pointer to a buffer that receives the desired information about the file
//                         The structure of the information returned in the buffer is defined by the FileInformationClass parameter.
// Length                - The size, in bytes, of the buffer pointed to by FileInformation.
// FileInformationClass  - The type of information to be returned about files in the directory.One of the following.
//                             FileBothDirectoryInformation     - FILE_BOTH_DIR_INFORMATION is returned
//                             FileDirectoryInformation         - FILE_DIRECTORY_INFORMATION is returned
//                             FileFullDirectoryInformation     - FILE_FULL_DIR_INFORMATION is returned
//                             FileIdBothDirectoryInformation   - FILE_ID_BOTH_DIR_INFORMATION is returned
//                             FileIdFullDirectoryInformation   - FILE_ID_FULL_DIR_INFORMATION is returned
//                             FileNamesInformation             - FILE_NAMES_INFORMATION is returned
//                             FileObjectIdInformation          - FILE_OBJECTID_INFORMATION is returned
//                             FileReparsePointInformation      - FILE_REPARSE_POINT_INFORMATION is returned
// ReturnSingleEntry     - Set to TRUE if only a single entry should be returned, FALSE otherwise.
// FileName              - An optional pointer to a caller-allocated Unicode string containing the name of a file (or multiple files, if wildcards are used)
//                         within the directory specified by FileHandle.This parameter is optional and can be NULL, in which case all files in the directory
//                         are returned.
// RestartScan           - Set to TRUE if the scan is to start at the first entry in the directory.Set to FALSE if resuming the scan from a previous call.
IMPLEMENTED(Detoured_NtQueryDirectoryFile)
NTSTATUS NTAPI Detoured_NtQueryDirectoryFile(
    _In_     HANDLE                 FileHandle,
    _In_opt_ HANDLE                 Event,
    _In_opt_ PIO_APC_ROUTINE        ApcRoutine,
    _In_opt_ PVOID                  ApcContext,
    _Out_    PIO_STATUS_BLOCK       IoStatusBlock,
    _Out_    PVOID                  FileInformation,
    _In_     ULONG                  Length,
    _In_     FILE_INFORMATION_CLASS FileInformationClass,
    _In_     BOOLEAN                ReturnSingleEntry,
    _In_opt_ PUNICODE_STRING        FileName,
    _In_     BOOLEAN                RestartScan)
{
    DetouredScope scope;
    LPCWSTR directoryName = nullptr;
    wstring filter;
    bool isEnumeration = true;
    CanonicalizedPath canonicalizedDirectoryPath;
    HandleOverlayRef overlay = nullptr;

    bool noDetour = scope.Detoured_IsDisabled();

    if (!noDetour)
    {
        // Check for enumeration. The default for us is true,
        // but if the FileName parameter is present and is not
        // a wild card, we'll set it to false.
        if (FileName != nullptr)
        {
            filter.assign(FileName->Buffer, (size_t)(FileName->Length / sizeof(wchar_t)));
            isEnumeration = PathContainsWildcard(filter.c_str());
        }

        // See if the handle is known
        overlay = TryLookupHandleOverlay(FileHandle);
        if (overlay == nullptr || overlay->EnumerationHasBeenReported)
        {
            noDetour = true;
        }
        else 
        {
            canonicalizedDirectoryPath = overlay->Policy.GetCanonicalizedPath();
            directoryName = canonicalizedDirectoryPath.GetPathString();

            if (_wcsicmp(directoryName, L"\\\\.\\MountPointManager") == 0 ||
                IsSpecialDeviceName(directoryName))
            {
                noDetour = true;
            }
        }
    }

    NTSTATUS result = Real_NtQueryDirectoryFile(
            FileHandle,
            Event,
            ApcRoutine,
            ApcContext,
            IoStatusBlock,
            FileInformation,
            Length,
            FileInformationClass,
            ReturnSingleEntry,
            FileName,
            RestartScan
            );

    // If we should not or cannot get info on the directory, we are done
    if (!noDetour)
    {
        // We should avoid doing anything interesting for non-directory handles.
        // What happens in practice is this:
        //   HANDLE h = NtCreateFile("\\?\C:\someDir\file")
        //   <access checked in NtCreateFile; maybe reported>
        //   NtQueryDirectoryFile(h)
        //   <fails somehow; h is not a directory handle>
        // If we instead went ahead and tried to report an enumeration in that case, we run into problems in report processing;
        // statically declared file dependencies have {Read} policy with {Report} actually masked out, and report
        // processing in fact assumes that the set of explicit reports do *not* contain such dependencies (i.e.
        // an access check is not repeated, so it is not discovered that read/probe is actually allowed).
        //
        // FindFirstFileEx handles this too, and performs a read-level access check if one tries to enumerate a file.
        // We don't have to worry about that at all here, since any necessary access check / report already happened
        // in CreateFile or NtCreateFile in order to get the (non)directory handle.
        if (overlay->Type == HandleType::Directory) 
        {
            
            // TODO: Perhaps should have a specific access check for enumeration.
            //       For now, we always allow enumeration and report it.
            //       Since enumeration has historically not been understood or reported at all, this is a fine incremental move -
            //       given a policy flag for allowing enumeration, we'd apply it globally anyway.
            // TODO: Should include the wildcard in enumeration reports, so that directory enumeration assertions can be more precise.

            PolicyResult directoryPolicyResult = overlay->Policy;
            
            // Only report the enumeration if specified by the policy
            bool reportDirectoryEnumeration = directoryPolicyResult.ReportDirectoryEnumeration();
            bool explicitlyReportDirectoryEnumeration = isEnumeration && reportDirectoryEnumeration;

            AccessCheckResult directoryAccessCheck(
                isEnumeration ? RequestedAccess::Enumerate : RequestedAccess::Probe,
                ResultAction::Allow,
                explicitlyReportDirectoryEnumeration ? ReportLevel::ReportExplicit : ReportLevel::Ignore);

            if (!explicitlyReportDirectoryEnumeration && ReportAnyAccess(false))
            {
                // Ensure access is reported (not explicit) when report all accesses is specified
                directoryAccessCheck.ReportLevel = ReportLevel::Report;
            }

            FileOperationContext fileOperationContext = FileOperationContext::CreateForRead(L"NtQueryDirectoryFile", directoryName);

            // Remember that we already enumerated this directory if successful
            overlay->EnumerationHasBeenReported = NT_SUCCESS(result) && directoryAccessCheck.ShouldReport();

            // We can report the status for directory now.
            ReportIfNeeded(directoryAccessCheck, fileOperationContext, overlay->Policy, (DWORD)(NT_SUCCESS(result) ? ERROR_SUCCESS : result), -1, filter.c_str());
        }
    }

    return result;
}

// Detoured_ZwQueryDirectoryFile
// See comments for Detoured_NtQueryDirectoryFile
IMPLEMENTED(Detoured_ZwQueryDirectoryFile)
NTSTATUS NTAPI Detoured_ZwQueryDirectoryFile(
    _In_     HANDLE                 FileHandle,
    _In_opt_ HANDLE                 Event,
    _In_opt_ PIO_APC_ROUTINE        ApcRoutine,
    _In_opt_ PVOID                  ApcContext,
    _Out_    PIO_STATUS_BLOCK       IoStatusBlock,
    _Out_    PVOID                  FileInformation,
    _In_     ULONG                  Length,
    _In_     FILE_INFORMATION_CLASS FileInformationClass,
    _In_     BOOLEAN                ReturnSingleEntry,
    _In_opt_ PUNICODE_STRING        FileName,
    _In_     BOOLEAN                RestartScan)
{
    DetouredScope scope;
    LPCWSTR directoryName = nullptr;
    wstring filter;
    bool isEnumeration = true;
    CanonicalizedPath canonicalizedDirectoryPath;
    HandleOverlayRef overlay = nullptr;

    // MonitorZwCreateOpenQueryFile allows disabling of ZwCreateFile, ZwOpenFile and ZwQueryDirectoryFile functions.
    bool noDetour = scope.Detoured_IsDisabled() || MonitorZwCreateOpenQueryFile();

    if (!noDetour)
    {
        // Check for enumeration. The default for us is true,
        // but if the FileName parameter is present and is not
        // a wild card, we'll set it to false.
        if (FileName != nullptr)
        {
            filter.assign(FileName->Buffer, (size_t)(FileName->Length / sizeof(wchar_t)));
            isEnumeration = PathContainsWildcard(filter.c_str());
        }

        // See if the handle is known
        overlay = TryLookupHandleOverlay(FileHandle);
        if (overlay == nullptr || overlay->EnumerationHasBeenReported)
        {
            noDetour = true;
        }
        else
        {
            canonicalizedDirectoryPath = overlay->Policy.GetCanonicalizedPath();
            directoryName = canonicalizedDirectoryPath.GetPathString();

            if (_wcsicmp(directoryName, L"\\\\.\\MountPointManager") == 0 ||
                IsSpecialDeviceName(directoryName))
            {
                noDetour = true;
            }
        }
    }

    NTSTATUS result = Real_ZwQueryDirectoryFile(
        FileHandle,
        Event,
        ApcRoutine,
        ApcContext,
        IoStatusBlock,
        FileInformation,
        Length,
        FileInformationClass,
        ReturnSingleEntry,
        FileName,
        RestartScan
    );

    // If we should not or cannot get info on the directory, we are done
    if (!noDetour)
    {
        // We should avoid doing anything interesting for non-directory handles.
        // What happens in practice is this:
        //   HANDLE h = ZwCreateFile("\\?\C:\someDir\file")
        //   <access checked in NtCreateFile; maybe reported>
        //   ZwQueryDirectoryFile(h)
        //   <fails somehow; h is not a directory handle>
        // If we instead went ahead and tried to report an enumeration in that case, we run into problems in report processing;
        // statically declared file dependencies have {Read} policy with {Report} actually masked out, and report
        // processing in fact assumes that the set of explicit reports do *not* contain such dependencies (i.e.
        // an access check is not repeated, so it is not discovered that read/probe is actually allowed).
        //
        // FindFirstFileEx handles this too, and performs a read-level access check if one tries to enumerate a file.
        // We don't have to worry about that at all here, since any necessary access check / report already happened
        // in CreateFile or ZtCreateFile in order to get the (non)directory handle.
        if (overlay->Type == HandleType::Directory) 
        {
            // TODO: Perhaps should have a specific access check for enumeration.
            //       For now, we always allow enumeration and report it.
            //       Since enumeration has historically not been understood or reported at all, this is a fine incremental move -
            //       given a policy flag for allowing enumeration, we'd apply it globally anyway.
            // TODO: Should include the wildcard in enumeration reports, so that directory enumeration assertions can be more precise.
            PolicyResult directoryPolicyResult = overlay->Policy;

            // Only report the enumeration if specified by the policy
            bool reportDirectoryEnumeration = directoryPolicyResult.ReportDirectoryEnumeration();
            bool explicitlyReportDirectoryEnumeration = isEnumeration && reportDirectoryEnumeration;

            AccessCheckResult directoryAccessCheck(
                isEnumeration ? RequestedAccess::Enumerate : RequestedAccess::Probe,
                ResultAction::Allow,
                explicitlyReportDirectoryEnumeration ? ReportLevel::ReportExplicit : ReportLevel::Ignore);

            if (!explicitlyReportDirectoryEnumeration && ReportAnyAccess(false))
            {
                // Ensure access is reported (not explicit) when report all accesses is specified
                directoryAccessCheck.ReportLevel = ReportLevel::Report;
            }

            FileOperationContext fileOperationContext = FileOperationContext::CreateForRead(L"ZwQueryDirectoryFile", directoryName);

            // Remember that we already enumerated this directory if successful
            overlay->EnumerationHasBeenReported = NT_SUCCESS(result) && directoryAccessCheck.ShouldReport();

            // We can report the status for directory now.
            ReportIfNeeded(directoryAccessCheck, fileOperationContext, overlay->Policy, (DWORD)(NT_SUCCESS(result) ? ERROR_SUCCESS : result));
        }
    }

    return result;
}

static bool PathFromObjectAttributesViaId(POBJECT_ATTRIBUTES attributes, CanonicalizedPath &path)
{
    DetouredScope scope;

    // Ensure Detours is disabled at this point.
    assert(scope.Detoured_IsDisabled());

    DWORD lastError = GetLastError();

    // Tool wants to open file by id, then that file is assumed to exist.
    // Unfortunately, we need to open a handle to get the file path.
    // Try open a handle with Read access.
    HANDLE hFile;
    IO_STATUS_BLOCK ioStatusBlock;

    NTSTATUS status = NtCreateFile(
        &hFile,
        FILE_GENERIC_READ,
        attributes,
        &ioStatusBlock,
        nullptr,
        FILE_ATTRIBUTE_NORMAL | FILE_ATTRIBUTE_REPARSE_POINT,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        FILE_OPEN,
        FILE_OPEN_BY_FILE_ID,
        nullptr,
        0);

    if (!NT_SUCCESS(status)) 
    {
        SetLastError(lastError);
        return false;
    }

    wstring fullPath;

    if (DetourGetFinalPathByHandle(hFile, fullPath) != ERROR_SUCCESS)
    {
        SetLastError(lastError);
        return false;
    }

    NtClose(hFile);
    path = CanonicalizedPath::Canonicalize(fullPath.c_str());
    
    SetLastError(lastError);

    return true;
}

// Helper function converts OBJECT_ATTRIBUTES into CanonicalizedPath
static bool PathFromObjectAttributes(POBJECT_ATTRIBUTES attributes, CanonicalizedPath &path, ULONG createOptions)
{
    if ((createOptions & FILE_OPEN_BY_FILE_ID) != 0)
    {
        return PathFromObjectAttributesViaId(attributes, path);
    }

    if (attributes->ObjectName == nullptr)
    {
        return false;
    }

    HandleOverlayRef overlay;

    // Check for the root directory
    if (attributes->RootDirectory != nullptr)
    {
        overlay = TryLookupHandleOverlay(attributes->RootDirectory);
        // If root directory is specified, we better know about it by know -- ignore unknown relative paths
        if (overlay == nullptr || overlay->Policy.GetCanonicalizedPath().IsNull())
        {
            return false;
        }
    }

    // Convert the ObjectName (buffer with a size) to be null-terminated.
    wstring name(attributes->ObjectName->Buffer, (size_t)(attributes->ObjectName->Length / sizeof(wchar_t)));

    if (overlay != nullptr)
    {
        // If there is no 'name' set (name is empty), just use the canonicalized path. Otherwise need to extend,
        // so '\' is appended to the canonicalized path and then the name is appended.
        path = name.empty() ? overlay->Policy.GetCanonicalizedPath() : overlay->Policy.GetCanonicalizedPath().Extend(name.c_str());
    }
    else
    {
        path = CanonicalizedPath::Canonicalize(name.c_str());
    }

    // Nt* functions require an NT-style path syntax. Opening 'C:\foo' will fail with STATUS_OBJECT_PATH_SYNTAX_BAD; 
    // instead something like '\??\C:\foo' or '\Device\HarddiskVolume1\foo' would work. If the caller provides a path
    // that couldn't be canonicalized or looks doomed to fail (not NT-style), we give up.
    // TODO: CanonicalizedPath may deserve an NT-specific Canonicalize equivalent (e.g. PathType::Win32Nt also matches \\?\, but that doesn't make sense here).
    return !path.IsNull() && (overlay != nullptr || path.Type == PathType::Win32Nt);
}

static DWORD MapNtCreateOptionsToWin32FileFlags(ULONG createOptions) 
{
    DWORD flags = 0;

    // We ignore most create options here, emphasizing just those that significantly affect semantics.
    flags |= (((createOptions & FILE_OPEN_FOR_BACKUP_INTENT) && !(createOptions & FILE_NON_DIRECTORY_FILE)) ? FILE_FLAG_BACKUP_SEMANTICS : 0);
    flags |= (createOptions & FILE_DELETE_ON_CLOSE ? FILE_FLAG_DELETE_ON_CLOSE : 0);
    flags |= (createOptions & FILE_OPEN_REPARSE_POINT ? FILE_FLAG_OPEN_REPARSE_POINT : 0);

    return flags;
}

static DWORD MapNtCreateDispositionToWin32Disposition(ULONG ntDisposition) 
{
    switch (ntDisposition) 
    {
    case FILE_CREATE:
        return CREATE_NEW;
    case FILE_OVERWRITE_IF:
        return CREATE_ALWAYS;
    case FILE_OPEN:
        return OPEN_EXISTING;
    case FILE_OPEN_IF:
        return OPEN_ALWAYS;
    case FILE_OVERWRITE: // For some reason, CreateFile(TRUNCATE_EXISTING) doesn't actually map to this (but something else may use it).
    case FILE_SUPERSEDE: // Technically this creates a new file rather than truncating.
        return TRUNCATE_EXISTING; 
    default:
        return 0;
    }
}

static bool CheckIfNtCreateMayDeleteFile(ULONG createOptions, ULONG access) 
{
    return (createOptions & FILE_DELETE_ON_CLOSE) != 0 || (access & DELETE) != 0;
}

// Some dispositions implicitly perform a write (truncate) or delete (supersede) inline;
// the write or delete is not required as part of the DesiredAccess mask though the filesystem will still (conditionally?) perform an access check anyway.
static bool CheckIfNtCreateDispositionImpliesWriteOrDelete(ULONG ntDisposition) 
{
    switch (ntDisposition) 
    {
    case FILE_OVERWRITE_IF:
    case FILE_OVERWRITE:
    case FILE_SUPERSEDE:
        return true;
    default:
        return false;
    }
}

// If FILE_DIRECTORY_FILE is specified, then only a directory will be opened / created (not a file).
static bool CheckIfNtCreateFileOptionsExcludeOpeningFiles(ULONG createOptions) 
{
    return (createOptions & FILE_DIRECTORY_FILE) != 0;
}

IMPLEMENTED(Detoured_ZwCreateFile)
NTSTATUS NTAPI Detoured_ZwCreateFile(
    _Out_    PHANDLE            FileHandle,
    _In_     ACCESS_MASK        DesiredAccess,
    _In_     POBJECT_ATTRIBUTES ObjectAttributes,
    _Out_    PIO_STATUS_BLOCK   IoStatusBlock,
    _In_opt_ PLARGE_INTEGER     AllocationSize,
    _In_     ULONG              FileAttributes,
    _In_     ULONG              ShareAccess,
    _In_     ULONG              CreateDisposition,
    _In_     ULONG              CreateOptions,
    _In_opt_ PVOID              EaBuffer,
    _In_     ULONG              EaLength)
{
    DetouredScope scope;

    // As a performance workaround, neuter the FILE_RANDOM_ACCESS hint (even if Detoured_IsDisabled() and there's another detoured API higher on the stack).
    // Prior investigations have shown that some tools do mention this hint, and as a result the cache manager holds on to pages more aggressively than
    // expected, even in very low memory conditions.
    CreateOptions &= ~FILE_RANDOM_ACCESS;

    CanonicalizedPath path;

    if (scope.Detoured_IsDisabled() ||
        !MonitorZwCreateOpenQueryFile() ||
        ObjectAttributes == nullptr ||
        !PathFromObjectAttributes(ObjectAttributes, path, CreateOptions) ||
        IsSpecialDeviceName(path.GetPathString()))
    {
        return Real_ZwCreateFile(
            FileHandle,
            DesiredAccess,
            ObjectAttributes,
            IoStatusBlock,
            AllocationSize,
            FileAttributes,
            ShareAccess,
            CreateDisposition,
            CreateOptions,
            EaBuffer,
            EaLength);
    }

    FileOperationContext opContext(
        L"ZwCreateFile",
        DesiredAccess,
        ShareAccess,
        MapNtCreateDispositionToWin32Disposition(CreateDisposition),
        MapNtCreateOptionsToWin32FileFlags(CreateOptions),
        path.GetPathString());

    PolicyResult policyResult;
    if (!policyResult.Initialize(path.GetPathString())) 
    {
        policyResult.ReportIndeterminatePolicyAndSetLastError(opContext);
        return DETOURS_STATUS_ACCESS_DENIED;
    }

    // We start with allow / ignore (no access requested) and then restrict based on read / write (maybe both, maybe neither!)
    AccessCheckResult accessCheck(RequestedAccess::None, ResultAction::Allow, ReportLevel::Ignore);
    bool forceReadOnlyForRequestedRWAccess = false;
    DWORD error = ERROR_SUCCESS;

    // Note that write operations are quite sneaky, and can perhaps be implied by any of options, dispositions, or desired access.
    // (consider FILE_DELETE_ON_CLOSE and FILE_OVERWRITE).
    // If we are operating on a directory, allow access - BuildXL allows accesses to directories (creation/deletion/etc.) always, as long as they are on a readable mount (at lease).
    if ((WantsWriteAccess(opContext.DesiredAccess) || 
         CheckIfNtCreateDispositionImpliesWriteOrDelete(CreateDisposition) || 
         CheckIfNtCreateMayDeleteFile(CreateOptions, DesiredAccess)) &&
        // Force directory checking using path, instead of handle, because the value of *FileHandle is still undefined, i.e., neither valid nor not valid.
        !IsHandleOrPathToDirectory(INVALID_HANDLE_VALUE, path.GetPathString(), false))
    {
        error = GetLastError();
        accessCheck = policyResult.CheckWriteAccess();

        // Note: The MonitorNtCreateFile() flag is temporary until OSG (we too) fixes all newly discovered dependencies.
        if (accessCheck.ResultAction != ResultAction::Allow && !MonitorNtCreateFile()) 
        {
            // TODO: As part of gradually turning on NtCreateFile detour reports, we currently only enforce deletes (some cmd builtins delete this way),
            //       and we ignore potential deletes on *directories* (specifically, robocopy likes to open target directories with delete access, without actually deleting them).
            if (!CheckIfNtCreateMayDeleteFile(CreateOptions, DesiredAccess)) 
            {
#if SUPER_VERBOSE
                Dbg(L"NtCreateFile: Ignoring a write-level access since it is not a delete: %s", policyResult.GetCanonicalizedPath().GetPathString());
#endif // SUPER_VERBOSE
                accessCheck = AccessCheckResult(RequestedAccess::None, ResultAction::Allow, ReportLevel::Ignore);
            }
            else if (CheckIfNtCreateFileOptionsExcludeOpeningFiles(CreateOptions)) 
            {
#if SUPER_VERBOSE
                Dbg(L"NtCreateFile: Ignoring a delete-level access since it will only apply to directories: %s", policyResult.GetCanonicalizedPath().GetPathString());
#endif // SUPER_VERBOSE
                accessCheck = AccessCheckResult(RequestedAccess::None, ResultAction::Allow, ReportLevel::Ignore);
            }
        }

        if (ForceReadOnlyForRequestedReadWrite() && accessCheck.ResultAction != ResultAction::Allow) 
        {
            // If ForceReadOnlyForRequestedReadWrite() is true, then we allow read for requested read-write access so long as the tool is allowed to read.
            // In such a case, we change the desired access to read only (see the call to Real_CreateFileW below).
            // As a consequence, the tool can fail if it indeed wants to write to the file.
            if (WantsReadAccess(DesiredAccess) && policyResult.AllowRead()) 
            {
                accessCheck = AccessCheckResult(RequestedAccess::Read, ResultAction::Allow, ReportLevel::Ignore);
                FileOperationContext operationContext(
                    L"ChangedReadWriteToReadAccess",
                    DesiredAccess,
                    ShareAccess,
                    MapNtCreateDispositionToWin32Disposition(CreateDisposition),
                    MapNtCreateOptionsToWin32FileFlags(CreateOptions),
                    path.GetPathString());

                ReportFileAccess(
                    operationContext,
                    FileAccessStatus_Allowed,
                    policyResult,
                    AccessCheckResult(RequestedAccess::None, ResultAction::Deny, ReportLevel::Report),
                    0,
                    -1);

                forceReadOnlyForRequestedRWAccess = true;
            }
        }

        if (!forceReadOnlyForRequestedRWAccess && accessCheck.ShouldDenyAccess()) 
        {
            ReportIfNeeded(accessCheck, opContext, policyResult, accessCheck.DenialError());
            return accessCheck.DenialNtStatus();
        }

        SetLastError(error);
    }

    // At this point and beyond, we know we are either dealing with a write request that has been approved, or a
    // read request which may or may not have been approved (due to special exceptions for directories and non-existent files).
    // It is safe to go ahead and perform the real NtCreateFile() call, and then to reason about the results after the fact.

    // Note that we need to add FILE_SHARE_DELETE to dwShareMode to leverage NTFS hardlinks to avoid copying cache
    // content, i.e., we need to be able to delete one of many links to a file. Unfortunately, share-mode is aggregated only per file
    // rather than per-link, so in order to keep unused links delete-able, we should ensure in-use links are delete-able as well.
    // However, adding FILE_SHARE_DELETE may be unexpected, for example, some unit tests may test for sharing violation. Thus,
    // we only add FILE_SHARE_DELETE if the file is tracked.

    // We also add FILE_SHARE_READ when it is safe to do so, since some tools accidentally ask for exclusive access on their inputs.

    DWORD desiredAccess = DesiredAccess;
    DWORD sharedAccess = ShareAccess;

    if (!policyResult.IndicateUntracked())
    {
        DWORD readSharingIfNeeded = policyResult.ShouldForceReadSharing(accessCheck) ? FILE_SHARE_READ : 0UL;
        desiredAccess = !forceReadOnlyForRequestedRWAccess ? desiredAccess : (desiredAccess & FILE_GENERIC_READ);
        sharedAccess = sharedAccess | readSharingIfNeeded | FILE_SHARE_DELETE;
    }
    
    error = ERROR_SUCCESS;

    NTSTATUS result = Real_ZwCreateFile(
        FileHandle,
        desiredAccess,
        ObjectAttributes,
        IoStatusBlock,
        AllocationSize,
        FileAttributes,
        sharedAccess,
        CreateDisposition,
        CreateOptions,
        EaBuffer,
        EaLength);

    error = GetLastError();
    
    if (!NT_SUCCESS(result))
    {
        // If we failed, just report. No need to execute anything below.
        FileReadContext readContext;
        readContext.InferExistenceFromNtStatus(result);

        // Note that 'handle' is allowed invalid for this check. Some tools poke at directories without
        // FILE_FLAG_BACKUP_SEMANTICS and so get INVALID_HANDLE_VALUE / ERROR_ACCESS_DENIED. In that kind of
        // case we have a fallback to re-probe.
        // We skip this to avoid the fallback probe if we don't believe the path exists, since increasing failed-probe volume is dangerous for perf.
        readContext.OpenedDirectory = 
            (readContext.FileExistence == FileExistence::Existent) 
            && ((CreateOptions & (FILE_DIRECTORY_FILE | FILE_NON_DIRECTORY_FILE)) == FILE_DIRECTORY_FILE 
                || IsHandleOrPathToDirectory(*FileHandle, path.GetPathString(), false));

        // Note: The MonitorNtCreateFile() flag is temporary until OSG (we too) fixes all newly discovered dependencies.
        if (MonitorNtCreateFile()) 
        {
            if (WantsReadAccess(opContext.DesiredAccess)) 
            {
                // We've now established all of the read context, which can further inform the access decision.
                // (e.g. maybe we we allow read only if the file doesn't exist).
                accessCheck = AccessCheckResult::Combine(accessCheck, policyResult.CheckReadAccess(RequestedReadAccess::Read, readContext));
            }
            else if (WantsProbeOnlyAccess(opContext.DesiredAccess)) 
            {
                accessCheck = AccessCheckResult::Combine(accessCheck, policyResult.CheckReadAccess(RequestedReadAccess::Probe, readContext));
            }
        }

        ReportIfNeeded(accessCheck, opContext, policyResult, RtlNtStatusToDosError(result));

        SetLastError(error);
        return result;
    }

    if (!IgnoreReparsePoints() && IsReparsePoint(path.GetPathString()) && !WantsProbeOnlyAccess(opContext.DesiredAccess))
    {
        // (1) Reparse point should not be ignored.
        // (2) File/Directory is a reparse point.
        // (3) Desired access is not probe only.
        // Note that handle can be invalid because users can CreateFileW of a symlink whose target is non-existent.
        NTSTATUS ntStatus;

        bool accessResult = EnforceChainOfReparsePointAccesses(
            policyResult.GetCanonicalizedPath(),
            (CreateOptions & FILE_OPEN_REPARSE_POINT) != 0 ? *FileHandle : INVALID_HANDLE_VALUE,
            desiredAccess,
            sharedAccess,
            CreateDisposition,
            FileAttributes,
            true,
            &ntStatus);

        if (!accessResult)
        {
            // If we don't have access to the target, close the handle to the reparse point.
            // This way we don't have a leaking handle.
            // (See below we the same when a normal file access is not allowed and close the file.)
            NtClose(*FileHandle);
            *FileHandle = INVALID_HANDLE_VALUE;
            ntStatus = DETOURS_STATUS_ACCESS_DENIED;

            return ntStatus;
        }
    }

    FileReadContext readContext;
    readContext.InferExistenceFromNtStatus(result);

    // Note that 'handle' is allowed invalid for this check. Some tools poke at directories without
    // FILE_FLAG_BACKUP_SEMANTICS and so get INVALID_HANDLE_VALUE / ERROR_ACCESS_DENIED. In that kind of
    // case we have a fallback to re-probe.
    // We skip this to avoid the fallback probe if we don't believe the path exists, since increasing failed-probe volume is dangerous for perf.
    readContext.OpenedDirectory = 
        (readContext.FileExistence == FileExistence::Existent) 
        && ((CreateOptions & (FILE_DIRECTORY_FILE | FILE_NON_DIRECTORY_FILE)) == FILE_DIRECTORY_FILE 
            || IsHandleOrPathToDirectory(*FileHandle, path.GetPathString(), false));

    // Note: The MonitorNtCreateFile() flag is temporary until OSG (we too) fixes all newly discovered dependencies.
    if (MonitorNtCreateFile()) 
    {
        if (WantsReadAccess(opContext.DesiredAccess)) 
        {
            // We've now established all of the read context, which can further inform the access decision.
            // (e.g. maybe we we allow read only if the file doesn't exist).
            accessCheck = AccessCheckResult::Combine(accessCheck, policyResult.CheckReadAccess(RequestedReadAccess::Read, readContext));
        }
        else if (WantsProbeOnlyAccess(opContext.DesiredAccess)) 
        {
            accessCheck = AccessCheckResult::Combine(accessCheck, policyResult.CheckReadAccess(RequestedReadAccess::Probe, readContext));
        }
    }

    ReportIfNeeded(accessCheck, opContext, policyResult, RtlNtStatusToDosError(result));

    bool hasValidHandle = result == ERROR_SUCCESS && !IsNullOrInvalidHandle(*FileHandle);
    if (accessCheck.ShouldDenyAccess()) 
    {
        error = accessCheck.DenialError();

        if (hasValidHandle) 
        {
            NtClose(*FileHandle);
        }

        *FileHandle = INVALID_HANDLE_VALUE;
        result = accessCheck.DenialNtStatus();
    }
    else if (hasValidHandle)
    {
        HandleType handleType = readContext.OpenedDirectory ? HandleType::Directory : HandleType::File;
        RegisterHandleOverlay(*FileHandle, accessCheck, policyResult, handleType);
    }

    SetLastError(error);
    return result;
}

IMPLEMENTED(Detoured_NtCreateFile)
NTSTATUS NTAPI Detoured_NtCreateFile(
    _Out_    PHANDLE            FileHandle,
    _In_     ACCESS_MASK        DesiredAccess,
    _In_     POBJECT_ATTRIBUTES ObjectAttributes,
    _Out_    PIO_STATUS_BLOCK   IoStatusBlock,
    _In_opt_ PLARGE_INTEGER     AllocationSize,
    _In_     ULONG              FileAttributes,
    _In_     ULONG              ShareAccess,
    _In_     ULONG              CreateDisposition,
    _In_     ULONG              CreateOptions,
    _In_opt_ PVOID              EaBuffer,
    _In_     ULONG              EaLength)
{
    DetouredScope scope;

    // As a performance workaround, neuter the FILE_RANDOM_ACCESS hint (even if Detoured_IsDisabled() and there's another detoured API higher on the stack).
    // Prior investigations have shown that some tools do mention this hint, and as a result the cache manager holds on to pages more aggressively than
    // expected, even in very low memory conditions.
    CreateOptions &= ~FILE_RANDOM_ACCESS;

    CanonicalizedPath path;
    
    if (scope.Detoured_IsDisabled() ||
        ObjectAttributes == nullptr ||
        !PathFromObjectAttributes(ObjectAttributes, path, CreateOptions) ||
        IsSpecialDeviceName(path.GetPathString()))
    {
        return Real_NtCreateFile(
            FileHandle,
            DesiredAccess,
            ObjectAttributes,
            IoStatusBlock,
            AllocationSize,
            FileAttributes,
            ShareAccess,
            CreateDisposition,
            CreateOptions,
            EaBuffer,
            EaLength);
    }

    DWORD error = ERROR_SUCCESS;

    FileOperationContext opContext(
        L"NtCreateFile",
        DesiredAccess,
        ShareAccess,
        MapNtCreateDispositionToWin32Disposition(CreateDisposition),
        MapNtCreateOptionsToWin32FileFlags(CreateOptions),
        path.GetPathString());

    PolicyResult policyResult;
    if (!policyResult.Initialize(path.GetPathString())) 
    {
        policyResult.ReportIndeterminatePolicyAndSetLastError(opContext);
        return DETOURS_STATUS_ACCESS_DENIED;
    }

    // We start with allow / ignore (no access requested) and then restrict based on read / write (maybe both, maybe neither!)
    AccessCheckResult accessCheck(RequestedAccess::None, ResultAction::Allow, ReportLevel::Ignore);
    bool forceReadOnlyForRequestedRWAccess = false;

    // Note that write operations are quite sneaky, and can perhaps be implied by any of options, dispositions, or desired access.
    // (consider FILE_DELETE_ON_CLOSE and FILE_OVERWRITE).
    // If we are operating on a directory, allow access - BuildXL allows accesses to directories (creation/deletion/etc.) always, as long as they are on a readable mount (at least).
    // TODO: Directory operation through NtCreateFile needs to be reviewed based on olkonone's work.
    //  - Users can call NtCreateFile directly to create directory. 
    //  - Commit 86e8274b by olkonone changes the way Detours validates directory creation. But the new validation is only applied to CreateDirectoryW.
    //  - Perhaps the validation should be done in NtCreateFile instead of in CreateDirectoryW.
    if ((WantsWriteAccess(opContext.DesiredAccess) || 
         CheckIfNtCreateDispositionImpliesWriteOrDelete(CreateDisposition) || 
         CheckIfNtCreateMayDeleteFile(CreateOptions, DesiredAccess)) &&
        // Force directory checking using path, instead of handle, because the value of *FileHandle is still undefined, i.e., neither valid nor not valid.
        !IsHandleOrPathToDirectory(INVALID_HANDLE_VALUE, path.GetPathString(), false))
    {
        error = GetLastError();
        accessCheck = policyResult.CheckWriteAccess();

        // Note: The MonitorNtCreateFile() flag is temporary until OSG (we too) fixes all newly discovered dependencies.
        if (accessCheck.ResultAction != ResultAction::Allow && !MonitorNtCreateFile()) 
        {
            // TODO: As part of gradually turning on NtCreateFile detour reports, we currently only enforce deletes (some cmd builtins delete this way),
            //       and we ignore potential deletes on *directories* (specifically, robocopy likes to open target directories with delete access, without actually deleting them).
            if (!CheckIfNtCreateMayDeleteFile(CreateOptions, DesiredAccess))
            {
#if SUPER_VERBOSE
                Dbg(L"NtCreateFile: Ignoring a write-level access since it is not a delete: %s", policyResult.GetCanonicalizedPath().GetPathString());
#endif // SUPER_VERBOSE
                accessCheck = AccessCheckResult(RequestedAccess::None, ResultAction::Allow, ReportLevel::Ignore);
            }
            else if (CheckIfNtCreateFileOptionsExcludeOpeningFiles(CreateOptions))
            {
#if SUPER_VERBOSE
                Dbg(L"NtCreateFile: Ignoring a delete-level access since it will only apply to directories: %s", policyResult.GetCanonicalizedPath().GetPathString());
#endif // SUPER_VERBOSE
                accessCheck = AccessCheckResult(RequestedAccess::None, ResultAction::Allow, ReportLevel::Ignore);
            }
        }

        if (ForceReadOnlyForRequestedReadWrite() && accessCheck.ResultAction != ResultAction::Allow)
        {
            // If ForceReadOnlyForRequestedReadWrite() is true, then we allow read for requested read-write access so long as the tool is allowed to read.
            // In such a case, we change the desired access to read only (see the call to Real_CreateFileW below).
            // As a consequence, the tool can fail if it indeed wants to write to the file.
            if (WantsReadAccess(DesiredAccess) && policyResult.AllowRead())
            {
                accessCheck = AccessCheckResult(RequestedAccess::Read, ResultAction::Allow, ReportLevel::Ignore);
                FileOperationContext operationContext(
                    L"ChangedReadWriteToReadAccess",
                    DesiredAccess,
                    ShareAccess,
                    MapNtCreateDispositionToWin32Disposition(CreateDisposition),
                    MapNtCreateOptionsToWin32FileFlags(CreateOptions),
                    path.GetPathString());

                ReportFileAccess(
                    operationContext,
                    FileAccessStatus_Allowed,
                    policyResult,
                    AccessCheckResult(RequestedAccess::None, ResultAction::Deny, ReportLevel::Report),
                    0,
                    -1);

                forceReadOnlyForRequestedRWAccess = true;
            }
        }

        if (!forceReadOnlyForRequestedRWAccess && accessCheck.ShouldDenyAccess())
        {
            ReportIfNeeded(accessCheck, opContext, policyResult, accessCheck.DenialError());
            return accessCheck.DenialNtStatus();
        }

        SetLastError(error);
    }

    // At this point and beyond, we know we are either dealing with a write request that has been approved, or a
    // read request which may or may not have been approved (due to special exceptions for directories and non-existent files).
    // It is safe to go ahead and perform the real NtCreateFile() call, and then to reason about the results after the fact.

    // Note that we need to add FILE_SHARE_DELETE to dwShareMode to leverage NTFS hardlinks to avoid copying cache
    // content, i.e., we need to be able to delete one of many links to a file. Unfortunately, share-mode is aggregated only per file
    // rather than per-link, so in order to keep unused links delete-able, we should ensure in-use links are delete-able as well.
    // However, adding FILE_SHARE_DELETE may be unexpected, for example, some unit tests may test for sharing violation. Thus,
    // we only add FILE_SHARE_DELETE if the file is tracked.

    // We also add FILE_SHARE_READ when it is safe to do so, since some tools accidentally ask for exclusive access on their inputs.

    DWORD desiredAccess = DesiredAccess;
    DWORD sharedAccess = ShareAccess;

    if (!policyResult.IndicateUntracked())
    {
        DWORD readSharingIfNeeded = policyResult.ShouldForceReadSharing(accessCheck) ? FILE_SHARE_READ : 0UL;
        desiredAccess = !forceReadOnlyForRequestedRWAccess ? desiredAccess : (desiredAccess & FILE_GENERIC_READ);
        sharedAccess = sharedAccess | readSharingIfNeeded | FILE_SHARE_DELETE;
    }
    
    error = ERROR_SUCCESS;

    NTSTATUS result = Real_NtCreateFile(
        FileHandle,
        desiredAccess,
        ObjectAttributes,
        IoStatusBlock,
        AllocationSize,
        FileAttributes,
        sharedAccess,
        CreateDisposition,
        CreateOptions,
        EaBuffer,
        EaLength);

    error = GetLastError();

    if (!NT_SUCCESS(result))
    {
        // If we failed, just report. No need to execute anything below.
        FileReadContext readContext;
        readContext.InferExistenceFromNtStatus(result);

        // Note that 'handle' is allowed invalid for this check. Some tools poke at directories without
        // FILE_FLAG_BACKUP_SEMANTICS and so get INVALID_HANDLE_VALUE / ERROR_ACCESS_DENIED. In that kind of
        // case we have a fallback to re-probe.
        // We skip this to avoid the fallback probe if we don't believe the path exists, since increasing failed-probe volume is dangerous for perf.
        readContext.OpenedDirectory = 
            (readContext.FileExistence == FileExistence::Existent) 
            && ((CreateOptions & (FILE_DIRECTORY_FILE | FILE_NON_DIRECTORY_FILE)) == FILE_DIRECTORY_FILE 
                || IsHandleOrPathToDirectory(*FileHandle, path.GetPathString(), false));

        // Note: The MonitorNtCreateFile() flag is temporary until OSG (we too) fixes all newly discovered dependencies.
        if (MonitorNtCreateFile())
        {
            if (WantsReadAccess(opContext.DesiredAccess))
            {
                // We've now established all of the read context, which can further inform the access decision.
                // (e.g. maybe we we allow read only if the file doesn't exist).
                accessCheck = AccessCheckResult::Combine(accessCheck, policyResult.CheckReadAccess(RequestedReadAccess::Read, readContext));
            }
            else if (WantsProbeOnlyAccess(opContext.DesiredAccess))
            {
                accessCheck = AccessCheckResult::Combine(accessCheck, policyResult.CheckReadAccess(RequestedReadAccess::Probe, readContext));
            }
        }

        ReportIfNeeded(accessCheck, opContext, policyResult, RtlNtStatusToDosError(result));

        SetLastError(error);

        return result;
    }

    if (!IgnoreReparsePoints() && IsReparsePoint(path.GetPathString()) && !WantsProbeOnlyAccess(opContext.DesiredAccess))
    {
        // (1) Reparse point should not be ignored.
        // (2) File/Directory is a reparse point.
        // (3) Desired access is not probe only.
        // Note that handle can be invalid because users can CreateFileW of a symlink whose target is non-existent.
        NTSTATUS ntStatus;

        bool accessResult = EnforceChainOfReparsePointAccesses(
            policyResult.GetCanonicalizedPath(),
            (CreateOptions & FILE_OPEN_REPARSE_POINT) != 0 ? *FileHandle : INVALID_HANDLE_VALUE,
            desiredAccess,
            sharedAccess,
            CreateDisposition,
            FileAttributes,
            true,
            &ntStatus);

        if (!accessResult)
        {
            // If we don't have access to the target, close the handle to the reparse point.
            // This way we don't have a leaking handle.
            // (See below we the same when a normal file access is not allowed and close the file.)
            NtClose(*FileHandle);
            
            *FileHandle = INVALID_HANDLE_VALUE;
            ntStatus = DETOURS_STATUS_ACCESS_DENIED;

            return ntStatus;
        }
    }

    FileReadContext readContext;
    readContext.InferExistenceFromNtStatus(result);

    // Note that 'handle' is allowed invalid for this check. Some tools poke at directories without
    // FILE_FLAG_BACKUP_SEMANTICS and so get INVALID_HANDLE_VALUE / ERROR_ACCESS_DENIED. In that kind of
    // case we have a fallback to re-probe.
    // We skip this to avoid the fallback probe if we don't believe the path exists, since increasing failed-probe volume is dangerous for perf.
    readContext.OpenedDirectory = 
        (readContext.FileExistence == FileExistence::Existent) 
        && ((CreateOptions & (FILE_DIRECTORY_FILE | FILE_NON_DIRECTORY_FILE)) == FILE_DIRECTORY_FILE 
            || IsHandleOrPathToDirectory(*FileHandle, path.GetPathString(), false));

    // Note: The MonitorNtCreateFile() flag is temporary until OSG (we too) fixes all newly discovered dependencies.
    if (MonitorNtCreateFile())
    {
        if (WantsReadAccess(opContext.DesiredAccess))
        {
            // We've now established all of the read context, which can further inform the access decision.
            // (e.g. maybe we we allow read only if the file doesn't exist).
            accessCheck = AccessCheckResult::Combine(accessCheck, policyResult.CheckReadAccess(RequestedReadAccess::Read, readContext));
        }
        else if (WantsProbeOnlyAccess(opContext.DesiredAccess))
        {
            accessCheck = AccessCheckResult::Combine(accessCheck, policyResult.CheckReadAccess(RequestedReadAccess::Probe, readContext));
        }
    }

    ReportIfNeeded(accessCheck, opContext, policyResult, RtlNtStatusToDosError(result));

    bool hasValidHandle = result == ERROR_SUCCESS && !IsNullOrInvalidHandle(*FileHandle);

    if (accessCheck.ShouldDenyAccess())
    {
        error = accessCheck.DenialError();

        if (hasValidHandle)
        {
            NtClose(*FileHandle);
        }

        *FileHandle = INVALID_HANDLE_VALUE;
        result = accessCheck.DenialNtStatus();
    }
    else if (hasValidHandle)
    {
        HandleType handleType = readContext.OpenedDirectory ? HandleType::Directory : HandleType::File;
        RegisterHandleOverlay(*FileHandle, accessCheck, policyResult, handleType);
    }

    SetLastError(error);

    return result;
}

// TODO: Why do we not simply call ZwCreateFile, just like NtOpenFile?
IMPLEMENTED(Detoured_ZwOpenFile)
NTSTATUS NTAPI Detoured_ZwOpenFile(
    _Out_ PHANDLE            FileHandle,
    _In_  ACCESS_MASK        DesiredAccess,
    _In_  POBJECT_ATTRIBUTES ObjectAttributes,
    _Out_ PIO_STATUS_BLOCK   IoStatusBlock,
    _In_  ULONG              ShareAccess,
    _In_  ULONG              OpenOptions)
{
    DetouredScope scope;

    CanonicalizedPath path;

    if (scope.Detoured_IsDisabled() ||
        !MonitorZwCreateOpenQueryFile() ||
        ObjectAttributes == nullptr ||
        !PathFromObjectAttributes(ObjectAttributes, path, OpenOptions) ||
        IsSpecialDeviceName(path.GetPathString()))
    {
        return Real_ZwOpenFile(
            FileHandle,
            DesiredAccess,
            ObjectAttributes,
            IoStatusBlock,
            ShareAccess,
            OpenOptions);
    }

    FileOperationContext opContext(
        L"ZwOpenFile",
        DesiredAccess,
        ShareAccess,
        MapNtCreateDispositionToWin32Disposition(FILE_OPEN),
        MapNtCreateOptionsToWin32FileFlags(OpenOptions),
        path.GetPathString());

    PolicyResult policyResult;
    if (!policyResult.Initialize(path.GetPathString()))
    {
        policyResult.ReportIndeterminatePolicyAndSetLastError(opContext);
        return DETOURS_STATUS_ACCESS_DENIED;
    }

    // We start with allow / ignore (no access requested) and then restrict based on read / write (maybe both, maybe neither!)
    AccessCheckResult accessCheck(RequestedAccess::None, ResultAction::Allow, ReportLevel::Ignore);
    bool forceReadOnlyForRequestedRWAccess = false;
    // Note that write operations are quite sneaky, and can perhaps be implied by any of options, dispositions, or desired access.
    // (consider FILE_DELETE_ON_CLOSE and FILE_OVERWRITE).
    // If we are operating on a directory, allow access - BuildXL allows accesses to directories (creation/deletion/etc.) always, as long as they are on a readable mount (at lease).
    if ((WantsWriteAccess(opContext.DesiredAccess) || 
         CheckIfNtCreateDispositionImpliesWriteOrDelete(FILE_OPEN) || 
         CheckIfNtCreateMayDeleteFile(OpenOptions, DesiredAccess)) &&
        // Force directory checking using path, instead of handle, because the value of *FileHandle is still undefined, i.e., neither valid nor not valid.
        !IsHandleOrPathToDirectory(INVALID_HANDLE_VALUE, path.GetPathString(), false))
    {
        accessCheck = policyResult.CheckWriteAccess();

        // Note: The MonitorNtCreateFile() flag is temporary until OSG (we too) fixes all newly discovered dependencies.
        if (accessCheck.ResultAction != ResultAction::Allow && !MonitorZwCreateOpenQueryFile())
        {
            // TODO: As part of gradually turning on NtCreateFile detour reports, we currently only enforce deletes (some cmd builtins delete this way),
            //       and we ignore potential deletes on *directories* (specifically, robocopy likes to open target directories with delete access, without actually deleting them).
            if (!CheckIfNtCreateMayDeleteFile(OpenOptions, DesiredAccess))
            {
#if SUPER_VERBOSE
                Dbg(L"NtCreateFile: Ignoring a write-level access since it is not a delete: %s", policyResult.GetCanonicalizedPath().GetPathString());
#endif // SUPER_VERBOSE
                accessCheck = AccessCheckResult(RequestedAccess::None, ResultAction::Allow, ReportLevel::Ignore);
            }
            else if (CheckIfNtCreateFileOptionsExcludeOpeningFiles(OpenOptions))
            {
#if SUPER_VERBOSE
                Dbg(L"NtCreateFile: Ignoring a delete-level access since it will only apply to directories: %s", policyResult.GetCanonicalizedPath().GetPathString());
#endif // SUPER_VERBOSE
                accessCheck = AccessCheckResult(RequestedAccess::None, ResultAction::Allow, ReportLevel::Ignore);
            }
        }

        if (ForceReadOnlyForRequestedReadWrite() && accessCheck.ResultAction != ResultAction::Allow)
        {
            // If ForceReadOnlyForRequestedReadWrite() is true, then we allow read for requested read-write access so long as the tool is allowed to read.
            // In such a case, we change the desired access to read only (see the call to Real_CreateFileW below).
            // As a consequence, the tool can fail if it indeed wants to write to the file.
            if (WantsReadAccess(DesiredAccess) && policyResult.AllowRead())
            {
                accessCheck = AccessCheckResult(RequestedAccess::Read, ResultAction::Allow, ReportLevel::Ignore);
                FileOperationContext operationContext(
                    L"ChangedReadWriteToReadAccess",
                    DesiredAccess,
                    ShareAccess,
                    MapNtCreateDispositionToWin32Disposition(FILE_OPEN),
                    MapNtCreateOptionsToWin32FileFlags(OpenOptions),
                    path.GetPathString());

                ReportFileAccess(
                    operationContext,
                    FileAccessStatus_Allowed,
                    policyResult,
                    AccessCheckResult(RequestedAccess::None, ResultAction::Deny, ReportLevel::Report),
                    0,
                    -1);

                forceReadOnlyForRequestedRWAccess = true;
            }
        }

        if (!forceReadOnlyForRequestedRWAccess && accessCheck.ShouldDenyAccess())
        {
            ReportIfNeeded(accessCheck, opContext, policyResult, accessCheck.DenialError());
            return accessCheck.DenialNtStatus();
        }
    }

    // At this point and beyond, we know we are either dealing with a write request that has been approved, or a
    // read request which may or may not have been approved (due to special exceptions for directories and non-existent files).
    // It is safe to go ahead and perform the real NtCreateFile() call, and then to reason about the results after the fact.

    // Note that we need to add FILE_SHARE_DELETE to dwShareMode to leverage NTFS hardlinks to avoid copying cache
    // content, i.e., we need to be able to delete one of many links to a file. Unfortunately, share-mode is aggregated only per file
    // rather than per-link, so in order to keep unused links delete-able, we should ensure in-use links are delete-able as well.
    // However, adding FILE_SHARE_DELETE may be unexpected, for example, some unit tests may test for sharing violation. Thus,
    // we only add FILE_SHARE_DELETE if the file is tracked.

    // We also add FILE_SHARE_READ when it is safe to do so, since some tools accidentally ask for exclusive access on their inputs.

    DWORD desiredAccess = DesiredAccess;
    DWORD sharedAccess = ShareAccess;

    if (!policyResult.IndicateUntracked())
    {
        DWORD readSharingIfNeeded = policyResult.ShouldForceReadSharing(accessCheck) ? FILE_SHARE_READ : 0UL;
        desiredAccess = !forceReadOnlyForRequestedRWAccess ? desiredAccess : (desiredAccess & FILE_GENERIC_READ);
        sharedAccess = sharedAccess | readSharingIfNeeded | FILE_SHARE_DELETE;
    }

    DWORD error = ERROR_SUCCESS;

    NTSTATUS result = Real_ZwOpenFile(
        FileHandle,
        DesiredAccess,
        ObjectAttributes,
        IoStatusBlock,
        ShareAccess,
        OpenOptions);

    error = GetLastError();

    if (!NT_SUCCESS(result))
    {
        // If we failed, just report. No need to execute anything below.
        FileReadContext readContext;
        readContext.InferExistenceFromNtStatus(result);

        // Note that 'handle' is allowed invalid for this check. Some tools poke at directories without
        // FILE_FLAG_BACKUP_SEMANTICS and so get INVALID_HANDLE_VALUE / ERROR_ACCESS_DENIED. In that kind of
        // case we have a fallback to re-probe.
        // We skip this to avoid the fallback probe if we don't believe the path exists, since increasing failed-probe volume is dangerous for perf.
        readContext.OpenedDirectory = 
            (readContext.FileExistence == FileExistence::Existent) 
            && ((OpenOptions & (FILE_DIRECTORY_FILE | FILE_NON_DIRECTORY_FILE)) == FILE_DIRECTORY_FILE 
                || IsHandleOrPathToDirectory(*FileHandle, path.GetPathString(), false));

        // Note: The MonitorNtCreateFile() flag is temporary until OSG (we too) fixes all newly discovered dependencies.
        if (MonitorZwCreateOpenQueryFile())
        {
            if (WantsReadAccess(opContext.DesiredAccess))
            {
                // We've now established all of the read context, which can further inform the access decision.
                // (e.g. maybe we we allow read only if the file doesn't exist).
                accessCheck = AccessCheckResult::Combine(accessCheck, policyResult.CheckReadAccess(RequestedReadAccess::Read, readContext));
            }
            else if (WantsProbeOnlyAccess(opContext.DesiredAccess))
            {
                accessCheck = AccessCheckResult::Combine(accessCheck, policyResult.CheckReadAccess(RequestedReadAccess::Probe, readContext));
            }
        }

        ReportIfNeeded(accessCheck, opContext, policyResult, RtlNtStatusToDosError(result));
        SetLastError(error);

        return result;
    }

    if (!IgnoreReparsePoints() && IsReparsePoint(path.GetPathString()) && !WantsProbeOnlyAccess(opContext.DesiredAccess))
    {
        // (1) Reparse point should not be ignored.
        // (2) File/Directory is a reparse point.
        // (3) Desired access is not probe only.
        // Note that handle can be invalid because users can CreateFileW of a symlink whose target is non-existent.
        NTSTATUS ntStatus;

        bool accessResult = EnforceChainOfReparsePointAccesses(
            policyResult.GetCanonicalizedPath(),
            (OpenOptions & FILE_OPEN_REPARSE_POINT) != 0 ? *FileHandle : INVALID_HANDLE_VALUE,
            desiredAccess,
            sharedAccess,
            FILE_OPEN,
            0L,
            true,
            &ntStatus);

        if (!accessResult)
        {
            // If we don't have access to the target, close the handle to the reparse point.
            // This way we don't have a leaking handle.
            // (See below we the same when a normal file access is not allowed and close the file.)
            NtClose(*FileHandle);
            *FileHandle = INVALID_HANDLE_VALUE;
            ntStatus = DETOURS_STATUS_ACCESS_DENIED;

            return ntStatus;
        }
    }

    FileReadContext readContext;
    readContext.InferExistenceFromNtStatus(result);

    // Note that 'handle' is allowed invalid for this check. Some tools poke at directories without
    // FILE_FLAG_BACKUP_SEMANTICS and so get INVALID_HANDLE_VALUE / ERROR_ACCESS_DENIED. In that kind of
    // case we have a fallback to re-probe.
    // We skip this to avoid the fallback probe if we don't believe the path exists, since increasing failed-probe volume is dangerous for perf.
    readContext.OpenedDirectory = 
        (readContext.FileExistence == FileExistence::Existent) 
        && ((OpenOptions & (FILE_DIRECTORY_FILE | FILE_NON_DIRECTORY_FILE)) == FILE_DIRECTORY_FILE 
            || IsHandleOrPathToDirectory(*FileHandle, path.GetPathString(), false));

    // Note: The MonitorNtCreateFile() flag is temporary until OSG (we too) fixes all newly discovered dependencies.
    if (MonitorZwCreateOpenQueryFile())
    {
        if (WantsReadAccess(opContext.DesiredAccess))
        {
            // We've now established all of the read context, which can further inform the access decision.
            // (e.g. maybe we we allow read only if the file doesn't exist).
            accessCheck = AccessCheckResult::Combine(accessCheck, policyResult.CheckReadAccess(RequestedReadAccess::Read, readContext));
        }
        else if (WantsProbeOnlyAccess(opContext.DesiredAccess))
        {
            accessCheck = AccessCheckResult::Combine(accessCheck, policyResult.CheckReadAccess(RequestedReadAccess::Probe, readContext));
        }
    }

    ReportIfNeeded(accessCheck, opContext, policyResult, RtlNtStatusToDosError(result));

    bool hasValidHandle = result == ERROR_SUCCESS && !IsNullOrInvalidHandle(*FileHandle);
    if (accessCheck.ShouldDenyAccess())
    {
        error = accessCheck.DenialError();

        if (hasValidHandle)
        {
            NtClose(*FileHandle);
        }

        *FileHandle = INVALID_HANDLE_VALUE;
        result = accessCheck.DenialNtStatus();
    }
    else if (hasValidHandle)
    {
        HandleType handleType = readContext.OpenedDirectory ? HandleType::Directory : HandleType::File;
        RegisterHandleOverlay(*FileHandle, accessCheck, policyResult, handleType);
    }

    SetLastError(error);

    return result;
}

IMPLEMENTED(Detoured_NtOpenFile)
NTSTATUS NTAPI Detoured_NtOpenFile(
    _Out_ PHANDLE            FileHandle,
    _In_  ACCESS_MASK        DesiredAccess,
    _In_  POBJECT_ATTRIBUTES ObjectAttributes,
    _Out_ PIO_STATUS_BLOCK   IoStatusBlock,
    _In_  ULONG              ShareAccess,
    _In_  ULONG              OpenOptions)
{
    // We don't EnterLoggingScope for NtOpenFile or NtCreateFile for two reasons:
    // - Of course these get called.
    // - It's hard to predict library loads (e.g. even by a statically linked CRT), which complicates testing of other call logging.

    // NtOpenFile is just a handy shortcut for NtCreateFile (with creation-specific parameters omitted).
    // We forward to the NtCreateFile detour here in order to have a single implementation.

    return Detoured_NtCreateFile(
        FileHandle,
        DesiredAccess,
        ObjectAttributes,
        IoStatusBlock,
        (PLARGE_INTEGER)NULL, // AllocationSize
        0L, // Attributes
        ShareAccess,
        FILE_OPEN,
        OpenOptions,
        (PVOID)NULL, // EaBuffer,
        0L // EaLength
        );
}

IMPLEMENTED(Detoured_NtClose)
NTSTATUS NTAPI Detoured_NtClose(_In_ HANDLE handle)
{
#if MEASURE_DETOURED_NT_CLOSE_IMPACT
    InterlockedIncrement(&g_ntCloseHandeCount);
#endif // MEASURE_DETOURED_NT_CLOSE_IMPACT

    // NtClose can be called in some surprising circumstances.
    // One that has arisen is in some particular exception handling stacks,
    // where KiUserExceptionDispatch is at the bottom; for some reason, the
    // TEB may have a null pointer for TLS, in which case querying Detoured_IsDisabled()
    // would AV. As a workaround, we just don't check it here (there's no harm in
    // dropping a handle overlay when trying to close the handle, anyway).
    //
    // Make sure the handle is closed after the object is marked for removal from the map.
    // This way the handle will never be assigned to a another object before removed from the map 
    // (whenever the map is accessed, the closed handle list is drained).

    if (!IsNullOrInvalidHandle(handle)) 
    {
        if (MonitorNtCreateFile())
        {
            // The map is cleared only if the MonitorNtCreateFile is on.
            // This is to make sure the behaviour for Windows builds is not altered.
            // Also if the NtCreateFile is no monitored, the map should not grow significantly. The other cases where it is updated -
            // for example CreateFileW, the map is updated by the CloseFile detoured API.
            if (UseExtraThreadToDrainNtClose())
            {
                AddClosedHandle(handle);
            }
            else
            {
                // Just remove the handle from the table directly.
                // Pass true for recursiveCall, since we don't have anything in the handle drain list and call to drain it is not needed.
                CloseHandleOverlay(handle, true);
            }
        }
    }

    return Real_NtClose(handle);
}

#undef IMPLEMENTED
