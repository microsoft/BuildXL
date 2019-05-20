// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "stdafx.h"

#include "DebuggingHelpers.h"
#include "DetoursHelpers.h"
#include "DetoursServices.h"
#include "globals.h"
#include "buildXL_mem.h"
#include "SendReport.h"
#include "StringOperations.h"
#include "DeviceMap.h"
#include "CanonicalizedPath.h"
#include "PolicyResult.h"
#include <list>
#include <string>
#include <stdio.h>
#include <stack>

using std::unique_ptr;
using std::basic_string;

// ----------------------------------------------------------------------------
// FUNCTION DEFINITIONS
// ----------------------------------------------------------------------------

/// <summary>
/// Gets the normalized (or subst'ed) path from a full path.
/// </summary>
/// <remarks>
/// The debug parameter is temporary to catch non-deterministic bug 1027027
/// </remarks>
void TranslateFilePath(_In_ const std::wstring& inFileName, _Out_ std::wstring& outFileName, _In_ bool debug)
{
    outFileName.assign(inFileName);

    if (g_pManifestTranslatePathTuples->empty()) 
    {
        // Nothing to translate.
        return;
    }

    // If the string coming in is null or empty, just return. No need to do anything.
    if (inFileName.empty() || inFileName.c_str() == nullptr)
    {
        return;
    }

    CanonicalizedPath canonicalizedPath = CanonicalizedPath::Canonicalize(inFileName.c_str());
    std::wstring tempStr(canonicalizedPath.GetPathString());
    
    // If the canonicalized string is null or empty, just return. No need to do anything.
    if (tempStr.empty() || tempStr.c_str() == nullptr)
    {
        return;
    }

    const std::wstring prefix(L"\\??\\");
    bool hasPrefix = !tempStr.compare(0, prefix.size(), prefix);

    const std::wstring prefixNt(L"\\\\?\\");
    bool hasPrefixNt = !tempStr.compare(0, prefixNt.size(), prefixNt);

    tempStr.assign(canonicalizedPath.GetPathStringWithoutTypePrefix());

    bool translated = false;
    bool needsTranslation = true;

    if (debug)
    {
        Dbg(L"TranslateFilePath-0: initial: '%s'", tempStr.c_str());
    }

    std::list<TranslatePathTuple*> manifestTranslatePathTuples(g_pManifestTranslatePathTuples->begin(), g_pManifestTranslatePathTuples->end());

    while (needsTranslation)
    {
        needsTranslation = false;
        size_t longestPath = 0;
        std::list<TranslatePathTuple*>::iterator replacementIt;

        std::wstring lowCaseFinalPath(tempStr);
        for (basic_string<wchar_t>::iterator p = lowCaseFinalPath.begin();
            p != lowCaseFinalPath.end(); ++p) {
            *p = towlower(*p);
        }

        // Find the longest path that can be used for translation from the g_pManifestTranslatePathTuples list.
        // Note: The g_pManifestTranslatePathTuples always comes canonicalized from the managed code.
        for (std::list<TranslatePathTuple*>::iterator it = manifestTranslatePathTuples.begin(); it != manifestTranslatePathTuples.end(); ++it)
        {
            TranslatePathTuple* tpTuple = *it;
            const std::wstring& lowCaseTargetPath = tpTuple->GetFromPath();
            size_t targetLen = lowCaseTargetPath.length();
            bool mayBeDirectoryPath = false;

            int comp = lowCaseFinalPath.compare(0, targetLen, lowCaseTargetPath);
            
            if (comp != 0)
            {
                // The path to be translated can be a directory path that does not have trailing '\\'.
                
                if (lowCaseFinalPath.back() != L'\\' 
                    && lowCaseTargetPath.back() == L'\\' 
                    && lowCaseFinalPath.length() == (targetLen - 1)) 
                {
                    std::wstring lowCaseFinalPathWithBs = lowCaseFinalPath + L'\\';
                    comp = lowCaseFinalPathWithBs.compare(0, targetLen, lowCaseTargetPath);
                    mayBeDirectoryPath = true;
                }
            }

            if (comp == 0)
            {
                if (longestPath < targetLen)
                {
                    replacementIt = it;
                    longestPath = !mayBeDirectoryPath ? targetLen : targetLen - 1;
                    translated = true;
                    needsTranslation = true;
                }
            }
        }

        // Translate using the longest translation path.
        if (needsTranslation)
        {
            TranslatePathTuple* replacementTuple = *replacementIt;

            std::wstring t(replacementTuple->GetToPath());
            t.append(tempStr, longestPath);

            if (debug)
            {
                Dbg(
                    L"TranslateFilePath-1: from: '%s', to '%s' (used mapping: '%s' --> '%s')",
                    tempStr.c_str(),
                    t.c_str(),
                    replacementTuple->GetFromPath().c_str(),
                    replacementTuple->GetToPath().c_str());
            }

            tempStr.assign(t);
            manifestTranslatePathTuples.erase(replacementIt);
        }
    }

    if (translated)
    {
        if (hasPrefix)
        {
            outFileName.assign(prefix);
        }
        else
        {
            if (hasPrefixNt)
            {
                outFileName.assign(prefixNt);
            }
            else
            {
                outFileName.assign(L"");
            }
        }

        outFileName.append(tempStr);

        if (debug)
        {
            Dbg(L"TranslateFilePath-2: final: '%s' --> '%s'", inFileName.c_str(), outFileName.c_str());
        }
    }
}

// Some perform file accesses, which don't yet fall into any configurable file access manifest category.
// These files now can be whitelisted, but there are already users deployed without the whitelisting feature
// that rely on these file accesses not blocked.
// These are some tools that use internal files or do some implicit directory creation, etc.
// In this list the tools are the CCI based set of products, csc compiler, resource compiler, build.exe trace log, etc.
// For such tools we allow file accesses on the special file patterns and report the access to BuildXL. BuildXL filters these
// accesses, but makes sure that there are reports for these accesses if some of them are declared as outputs.
bool GetSpecialCaseRulesForSpecialTools(
    __in  PCWSTR absolutePath,
    __in  size_t absolutePathLength,
    __out FileAccessPolicy& policy)
{
    assert(absolutePath);
    assert(absolutePathLength == wcslen(absolutePath));

    switch (GetProcessKind())
    {
    case SpecialProcessKind::Csc:
    case SpecialProcessKind::Cvtres:
    case SpecialProcessKind::Resonexe:
        // Some tools emit temporary files into the same directory
        // as the final output file.
        if (HasSuffix(absolutePath, absolutePathLength, L".tmp")) {
#if SUPER_VERBOSE
            Dbg(L"special case: temp file: %s", absolutePath);
#endif // SUPER_VERBOSE
            int intPolicy = (int)policy | (int)FileAccessPolicy_AllowAll;
            policy = (FileAccessPolicy)intPolicy;
            return true;
        }
        break;

    case SpecialProcessKind::RC:
        // The native resource compiler (RC) emits temporary files into the same
        // directory as the final output file.
        if (StringLooksLikeRCTempFile(absolutePath, absolutePathLength)) {
#if SUPER_VERBOSE
            Dbg(L"special case: temp file: %s", absolutePath);
#endif // SUPER_VERBOSE
            int intPolicy = (int)policy | (int)FileAccessPolicy_AllowAll;
            policy = (FileAccessPolicy)intPolicy;
            return true;
        }
        break;

    case SpecialProcessKind::Mt:
        // The Mt tool emits temporary files into the same directory as the final output file.
        if (StringLooksLikeMtTempFile(absolutePath, absolutePathLength, L".tmp")) {
#if SUPER_VERBOSE
            Dbg(L"special case: temp file: %s", absolutePath);
#endif // SUPER_VERBOSE
            int intPolicy = (int)policy | (int)FileAccessPolicy_AllowAll;
            policy = (FileAccessPolicy)intPolicy;
            return true;
        }
        break;

    case SpecialProcessKind::CCCheck:
    case SpecialProcessKind::CCDocGen:
    case SpecialProcessKind::CCRefGen:
    case SpecialProcessKind::CCRewrite:
        // The cc-line of tools like to find pdb files by using the pdb path embedded in a dll/exe. 
        // If the dll/exe was built with different roots, then this results in somewhat random file accesses.
        if (HasSuffix(absolutePath, absolutePathLength, L".pdb")) {
#if SUPER_VERBOSE
            Dbg(L"special case: pdb file: %s", absolutePath);
#endif // SUPER_VERBOSE
            int intPolicy = (int)policy | (int)FileAccessPolicy_AllowAll;
            policy = (FileAccessPolicy)intPolicy;
            return true;
        }
        break;

    case SpecialProcessKind::WinDbg:
    case SpecialProcessKind::NotSpecial:
        // no special treatment
        break;
    }

    // build.exe and tracelog.dll capture dependency information in temporary files in the object root called _buildc_dep_out.<pass#>
    if (StringLooksLikeBuildExeTraceLog(absolutePath, absolutePathLength)) {
        int intPolicy = (int)policy | (int)FileAccessPolicy_AllowAll;
        policy = (FileAccessPolicy)intPolicy;
#if SUPER_VERBOSE
        Dbg(L"Build.exe trace log path: %s", absolutePath);
#endif // SUPER_VERBOSE
        return true;
    }

    return false;
}

// This functions allows file accesses for special undeclared files.
// In the special set set we include:
//     1. Code coverage runs
//     2. Te drive devices
//     3. Dos devices and special system devices/names (pipes, null dev etc).
// These accesses now should be white listed, but many users have deployed products that have specs not declaring such accesses.
bool GetSpecialCaseRulesForCoverageAndSpecialDevices(
    __in  PCWSTR absolutePath,
    __in  size_t absolutePathLength,
    __in  PathType pathType,
    __out FileAccessPolicy& policy)
{
    assert(absolutePath);
    assert(absolutePathLength == wcslen(absolutePath));

    // When running test cases with Code Coverage enabled, some more files are loaded that we should ignore
    if (IgnoreCodeCoverage()) {
        if (HasSuffix(absolutePath, absolutePathLength, L".pdb") ||
            HasSuffix(absolutePath, absolutePathLength, L".nls") ||
            HasSuffix(absolutePath, absolutePathLength, L".dll"))
        {
#if SUPER_VERBOSE
            Dbg(L"Ignoring possibly code coverage related path: %s", absolutePath);
#endif // SUPER_VERBOSE
            int intPolicy = (int)policy | (int)FileAccessPolicy_AllowAll;
            policy = (FileAccessPolicy)intPolicy;
            return true;
        }
    }

    if (pathType == PathType::LocalDevice || pathType == PathType::Win32Nt) {
        bool maybeStartsWithDrive = absolutePathLength >= 2 && IsDriveLetter(absolutePath[0]) && absolutePath[1] == L':';

        // For a normal Win32 path, C: means C:<current directory on C> or C:\ if one is not set. But \\.\C:, \\?\C:, and \??\C:
        // mean 'the device C:'. We don't care to model access to devices (volumes in this case).
        if (maybeStartsWithDrive && absolutePathLength == 2) {
#if SUPER_VERBOSE
            Dbg(L"Ignoring access to drive device (not the volume root; missing a trailing slash): %s", absolutePath);
#endif // SUPER_VERBOSE
            policy = FileAccessPolicy_AllowAll;
            return true;
        }

        // maybeStartsWithDrive => absolutePathLength >= 3
        assert(!maybeStartsWithDrive || absolutePathLength >= 3);

        // We do not provide a special case for e.g. \\.\C:\foo (equivalent to the Win32 C:\foo) but we do want to allow access
        // to non-drive DosDevices. For example, the Windows DNS API ends up(indirectly) calling CreateFile("\\\\.\\Nsi").
        // Note that this also allows access to the named pipe filesystem under \\.\pipe.
        bool startsWithDriveRoot = maybeStartsWithDrive && absolutePath[2] == L'\\';
        if (!startsWithDriveRoot) {
#if SUPER_VERBOSE
            Dbg(L"Ignoring non-drive device path: %s", absolutePath);
#endif // SUPER_VERBOSE
            policy = FileAccessPolicy_AllowAll;
            return true;
        }
    }

    if (IsPathToNamedStream(absolutePath, absolutePathLength)) {
#if SUPER_VERBOSE
        Dbg(L"Ignoring path to a named stream: %s", absolutePath);
#endif // SUPER_VERBOSE
        policy = FileAccessPolicy_AllowAll;
        return true;
    }
    
    return false;
}

bool WantsWriteAccess(DWORD access)
{
    return (access & (GENERIC_ALL | GENERIC_WRITE | DELETE | FILE_WRITE_DATA | FILE_WRITE_ATTRIBUTES | FILE_WRITE_EA | FILE_APPEND_DATA)) != 0;
}

bool WantsReadAccess(DWORD access)
{
    return (access & (GENERIC_READ | FILE_READ_DATA)) != 0;
}

bool WantsReadOnlyAccess(DWORD access)
{
    return WantsReadAccess(access) && !WantsWriteAccess(access);
}

bool WantsProbeOnlyAccess(DWORD access)
{
    return !WantsReadAccess(access)
        && !WantsWriteAccess(access)
        && (access == 0 || (access & (FILE_READ_ATTRIBUTES | FILE_READ_EA)) != 0);
}

/* Indicates if a path contains a wildcard that may be interpreted by FindFirstFile / FindFirstFileEx. */
bool PathContainsWildcard(LPCWSTR path) {
    for (WCHAR const* pch = path; *pch != L'\0'; pch++) {
        if (*pch == L'?' || *pch == L'*') {
            return true;
        }
    }

    return false;
}

bool ParseUInt64Arg(
    __inout PCWSTR& pos,
    int radix,
    __out ulong& value)
{
    PWSTR nextPos;
    value = _wcstoui64(pos, &nextPos, radix);
    if (nextPos == NULL) {
        return false;
    }

    if (*nextPos == L',') {
        ++nextPos;
    }
    else if (*nextPos != 0) {
        return false;
    }

    pos = nextPos;
    return true;
}

bool LocateFileAccessManifest(
    __out const void*& manifest,
    __out DWORD& manifestSize)
{
    manifest = NULL;
    manifestSize = 0;

    HMODULE previousModule = NULL;
    for (;;) {
        HMODULE currentModule = DetourEnumerateModules(previousModule);
        if (currentModule == NULL) {
            Dbg(L"Did not find Detours payload.");
            return false;
        }

        previousModule = currentModule;
        DWORD payloadSize;
        const void* payload = DetourFindPayload(currentModule, __uuidof(IDetourServicesManifest), &payloadSize);
        if (payload != NULL) {
#if SUPER_VERBOSE
            Dbg(L"Found Detours payload at %p len 0x%x", payload, payloadSize);
#endif // SUPER_VERBOSE
            manifest = payload;
            manifestSize = payloadSize;
            return true;
        }
    }
}

/// VerifyManifestTree
///
/// Run through the tree and perform integrity checks on everything reachable in the tree,
/// to detect the possibility of data corruption in the tree.
/// 
/// This check is O(m) where m is the number of entries in the manifest.
/// Only use it for debugging when a corrupted binary structure is suspected.
#pragma warning( push )
#pragma warning( disable: 4100 ) // in release builds, record is unused
inline void VerifyManifestTree(PCManifestRecord const record)
{
#ifdef _DEBUG
    record->AssertValid();

    // loop through every item on every level recursively and verify tags are correct
    ManifestRecord::BucketCountType numBuckets = record->BucketCount;
    for (ManifestRecord::BucketCountType i = 0; i < numBuckets; i++)
    {
        PCManifestRecord child = record->GetChildRecord(i);

        if (child != nullptr)
        {
            VerifyManifestTree(child);
        }
    }
#endif
}
#pragma warning( pop )

/// VerifyManifestRoot
///
/// Check that the root is a valid root record by checking the tag and that
/// the path of the root scope is an empty string.
#pragma warning( push )
#pragma warning( disable: 4100 ) // in release builds, root is unused
inline void VerifyManifestRoot(PCManifestRecord const root)
{
#ifdef _DEBUG
    root->AssertValid();
#endif

    assert(root->GetPartialPath()[0] == 0); // the root path should be an empty string
}
#pragma warning( pop )

void WriteToInternalErrorsFile(PCWSTR format, ...)
{
    wprintf(L"Logging internal error message from Detours...\r\n");
    if (g_internalDetoursErrorNotificationFile != nullptr)
    {
        DWORD error = GetLastError();

        while (true)
        {
            // Get a file handle.
            HANDLE openedFile = CreateFileW(g_internalDetoursErrorNotificationFile,
                GENERIC_WRITE,
                0,
                NULL,
                OPEN_ALWAYS,
                FILE_ATTRIBUTE_NORMAL,
                NULL);

            if (openedFile == INVALID_HANDLE_VALUE)
            {
                // Wait to get exclusive access to the file.
                if (GetLastError() == ERROR_SHARING_VIOLATION)
                {
                    Sleep(10);
                    continue;
                }

                // Failure to open the file. if that happens, we miss logging this message log, so just continue.
                break;
            }
            else
            {
                // File was successfully opened --> format error message and write it to file
                va_list args;
                va_start(args, format);
                std::wstring errorMessage = DebugStringFormatArgs(format, args);
                WriteFile(openedFile, errorMessage.c_str(), (DWORD) errorMessage.length(), nullptr, nullptr);
                va_end(args);
                CloseHandle(openedFile);

                break;
            }
        }

        SetLastError(error);
    }
}

inline uint32_t ParseUint32(const byte *payloadBytes, size_t &offset)
{
    uint32_t i = *(uint32_t*)(&payloadBytes[offset]);
    offset += sizeof(uint32_t);
    return i;
}

/// Decodes a length plus UTF-16 non-null-terminated string written by FileAccessManifest.WriteChars()
/// into an allocated, null-terminated string. Returns nullptr if the encoded string length is zero.
wchar_t *CreateStringFromWriteChars(const byte *payloadBytes, size_t &offset, uint32_t *pStrLen = nullptr)
{
    uint32_t len = ParseUint32(payloadBytes, offset);
    if (pStrLen != nullptr)
    {
        *pStrLen = len;
    }

    WCHAR *pStr = nullptr;
    if (len != 0)
    {
        pStr = new wchar_t[len + 1]; // Reserve some space for \0 terminator at end.
        uint32_t strSizeBytes = sizeof(wchar_t) * (len + 1);
        ZeroMemory((void*)pStr, strSizeBytes);
        memcpy_s((void*)pStr, strSizeBytes, (wchar_t*)(&payloadBytes[offset]), sizeof(wchar_t) * len);
        offset += sizeof(wchar_t) * len;
    }

    return pStr;
}

bool ParseFileAccessManifest(
    const void* payload,
    DWORD)
{
    if (g_manifestPtr != nullptr) {
        // Fail if the pointer is not null. We are loading the Dll, so we could have not loaded this yet.
        wprintf(L"g_manifestPtr already set - %p", g_manifestPtr);
        fwprintf(stderr, L"g_manifestPtr already set - %p", g_manifestPtr);
        HandleDetoursInjectionAndCommunicationErrors(DETOURS_PAYLOAD_PARSE_FAILED_9, L"g_manifestPtr already set: exit(-51).", DETOURS_WINDOWS_LOG_MESSAGE_9);
        return false;
    }

    //
    // Parse the file access manifest payload
    //

    assert(payload != nullptr);

    std::wstring initErrorMessage;
    if (!g_pDetouredProcessInjector->Init(reinterpret_cast<const byte *>(payload), initErrorMessage)) 
    {
        // Error initializing injector due to incorrect content of payload.
        std::wstring initError = L"Error initializing process injector: ";
        initError.append(initErrorMessage);
        wprintf(L"%s", initError.c_str());
        fwprintf(stderr, L"%s", initError.c_str());

        std::wstring initErrorWithExitCode(initError);
        initErrorWithExitCode.append(L": exit(-61).");

        HandleDetoursInjectionAndCommunicationErrors(DETOURS_PAYLOAD_PARSE_FAILED_19, initErrorWithExitCode.c_str(), DETOURS_WINDOWS_LOG_MESSAGE_19);
        return false;
    }

    const byte * const payloadBytes = g_pDetouredProcessInjector->Payload();
    const DWORD payloadSize = g_pDetouredProcessInjector->PayloadSize();

    assert(payloadSize > 0);
    assert(payloadBytes != nullptr);

    g_manifestPtr = VirtualAlloc(nullptr, payloadSize, MEM_COMMIT, PAGE_READWRITE);
    g_manifestSizePtr = (PDWORD)VirtualAlloc(nullptr, sizeof(DWORD), MEM_COMMIT, PAGE_READWRITE);
    if (g_manifestPtr == nullptr || g_manifestSizePtr == nullptr)
    {
        // Error allocating memory.
        wprintf(L"Error allocating virtual memory.");
        fwprintf(stderr, L"Error allocating virtual memory");
        HandleDetoursInjectionAndCommunicationErrors(DETOURS_PAYLOAD_PARSE_FAILED_10, L"Error allocating virtual memory: exit(-52).", DETOURS_WINDOWS_LOG_MESSAGE_10);
        return false;
    }

    if (memcpy_s(g_manifestPtr, payloadSize, payloadBytes, payloadSize))
    {
        // Could't copy the payload.
        wprintf(L"Error copying payload to virtual memory.");
        fwprintf(stderr, L"Error copying payload to virtual memory");
        HandleDetoursInjectionAndCommunicationErrors(DETOURS_PAYLOAD_PARSE_FAILED_11, L"Error copying payload to virtual memory: exit(-53).", DETOURS_WINDOWS_LOG_MESSAGE_11);
        return false;
    }

    *g_manifestSizePtr = payloadSize;

    DWORD oldProtection = 0;
    if (VirtualProtect(g_manifestPtr, payloadSize, PAGE_READONLY, &oldProtection) == 0)
    {
        // Error protecting the memory for the payload.
        wprintf(L"Error protecting payload in virtual memory.");
        fwprintf(stderr, L"Error protecting payload in virtual memory");
        HandleDetoursInjectionAndCommunicationErrors(DETOURS_PAYLOAD_PARSE_FAILED_12, L"Error protecting payload in virtual memory: exit(-54).", DETOURS_WINDOWS_LOG_MESSAGE_12);
        return false;
    }
    
    if (VirtualProtect(g_manifestSizePtr, sizeof(DWORD), PAGE_READONLY, &oldProtection) == 0)
    {
        // Error protecting the memory for the payloadSize.
        wprintf(L"Error protecting payload size in virtual memory.");
        fwprintf(stderr, L"Error protecting payload size in virtual memory");
        HandleDetoursInjectionAndCommunicationErrors(DETOURS_PAYLOAD_PARSE_FAILED_13, L"Error protecting payload size in virtual memory: exit(-55).", DETOURS_WINDOWS_LOG_MESSAGE_13);
        return false;
    }

    g_currentProcessId = GetCurrentProcessId();

    g_currentProcessCommandLine = GetCommandLine();

    g_lpDllNameX86 = NULL;
    g_lpDllNameX64 = NULL;

    if (*g_manifestSizePtr <= sizeof(size_t))
    {
        wprintf(L"Error bad payload size %d:%llu.", (int)*g_manifestSizePtr, (unsigned long long)sizeof(size_t));
        fwprintf(stderr, L"Error bad payload size %d:%llu.", (int)*g_manifestSizePtr, (unsigned long long)sizeof(size_t));
        HandleDetoursInjectionAndCommunicationErrors(DETOURS_PAYLOAD_PARSE_FAILED_14, L"Error bad payload size: exit(-56).", DETOURS_WINDOWS_LOG_MESSAGE_14);
        return false;
    }

    size_t offset = 0;

    PCManifestDebugFlag debugFlag = reinterpret_cast<PCManifestDebugFlag>(&payloadBytes[offset]);
    if (!debugFlag->CheckValidityAndHandleInvalid())
    {
        wprintf(L"Error invalid debugFlag.");
        fwprintf(stderr, L"Error invalid debugFlag.");
        HandleDetoursInjectionAndCommunicationErrors(DETOURS_PAYLOAD_PARSE_FAILED_15, L"Error invalid debugFlag: exit(-57).", DETOURS_WINDOWS_LOG_MESSAGE_15);
        return false;
    }

    offset += debugFlag->GetSize();

    PCManifestInjectionTimeout injectionTimeoutFlag = reinterpret_cast<PCManifestInjectionTimeout>(&payloadBytes[offset]);
    if (!injectionTimeoutFlag->CheckValidityAndHandleInvalid())
    {
        wprintf(L"Error invalid injectionTimeoutFlag.");
        fwprintf(stderr, L"Error invalid injectionTimeoutFlag.");
        HandleDetoursInjectionAndCommunicationErrors(DETOURS_PAYLOAD_PARSE_FAILED_16, L"Error invalid injectionTimeoutFlag: exit(-58).", DETOURS_WINDOWS_LOG_MESSAGE_16);
        return false;
    }

    g_injectionTimeoutInMinutes = static_cast<unsigned long>(injectionTimeoutFlag->Flags);

    // Make sure the injectionTimeout is not less than 10 min.
    if (g_injectionTimeoutInMinutes < 10)
    {
        g_injectionTimeoutInMinutes = 10;
    }

    offset += injectionTimeoutFlag->GetSize();

    g_manifestTranslatePathsStrings = reinterpret_cast<const PManifestTranslatePathsStrings>(&payloadBytes[offset]);
    g_manifestTranslatePathsStrings->AssertValid();
#ifdef _DEBUG
    offset += sizeof(uint32_t);
#endif
    uint32_t manifestTranslatePathsSize = ParseUint32(payloadBytes, offset);
    for (uint32_t i = 0; i < manifestTranslatePathsSize; i++)
    {
        uint32_t manifestTranslatePathsFromSize = ParseUint32(payloadBytes, offset);
        std::wstring translateFrom;
        translateFrom.assign(L"");
        if (manifestTranslatePathsFromSize > 0)
        {
            translateFrom.append((wchar_t*)(&payloadBytes[offset]), manifestTranslatePathsFromSize);

            for (basic_string<wchar_t>::iterator p = translateFrom.begin();
                p != translateFrom.end(); ++p) 
            {
                *p = towlower(*p);
            }

            offset += sizeof(WCHAR) * manifestTranslatePathsFromSize;
        }

        uint32_t manifestTranslatePathsToSize = ParseUint32(payloadBytes, offset);
        std::wstring translateTo;
        translateTo.assign(L"");
        if (manifestTranslatePathsToSize > 0)
        {
            translateTo.append((wchar_t*)(&payloadBytes[offset]), manifestTranslatePathsToSize);
            offset += sizeof(WCHAR) * manifestTranslatePathsToSize;
        }

        if (!translateFrom.empty() && !translateTo.empty())
        {
            g_pManifestTranslatePathTuples->push_back(new TranslatePathTuple(translateFrom, translateTo));
        }
    }

    g_manifestInternalDetoursErrorNotificationFileString = reinterpret_cast<const PManifestInternalDetoursErrorNotificationFileString>(&payloadBytes[offset]);
    g_manifestInternalDetoursErrorNotificationFileString->AssertValid();
#ifdef _DEBUG
    offset += sizeof(uint32_t);
#endif
    uint32_t manifestInternalDetoursErrorNotificationFileSize;
    g_internalDetoursErrorNotificationFile = CreateStringFromWriteChars(payloadBytes, offset, &manifestInternalDetoursErrorNotificationFileSize);

    PCManifestFlags flags = reinterpret_cast<PCManifestFlags>(&payloadBytes[offset]);
    flags->AssertValid();
    g_fileAccessManifestFlags = static_cast<FileAccessManifestFlag>(flags->Flags);
    offset += flags->GetSize();

    PCManifestExtraFlags extraFlags = reinterpret_cast<PCManifestExtraFlags>(&payloadBytes[offset]);
    extraFlags->AssertValid();
    g_fileAccessManifestExtraFlags = static_cast<FileAccessManifestExtraFlag>(extraFlags->ExtraFlags);
    offset += extraFlags->GetSize();

    PCManifestPipId pipId = reinterpret_cast<PCManifestPipId>(&payloadBytes[offset]);
    pipId->AssertValid();
    g_FileAccessManifestPipId = static_cast<uint64_t>(pipId->PipId);
    offset += pipId->GetSize();

    // Semaphore names don't allow '\\'
    if (CheckDetoursMessageCount() && g_internalDetoursErrorNotificationFile != nullptr)
    {
        wchar_t* helperString = new wchar_t[manifestInternalDetoursErrorNotificationFileSize + 1];
        ZeroMemory((void*)helperString, sizeof(wchar_t) * (manifestInternalDetoursErrorNotificationFileSize + 1));

        for (uint32_t i = 0; i < manifestInternalDetoursErrorNotificationFileSize; i++)
        {
            if (g_internalDetoursErrorNotificationFile[i] == L'\\')
            {
                helperString[i] = L'_';
            }
            else
            {
                helperString[i] = g_internalDetoursErrorNotificationFile[i];
            }
        }

        g_messageCountSemaphore = OpenSemaphore(SEMAPHORE_ALL_ACCESS, FALSE, helperString);

        if (g_messageCountSemaphore == nullptr || g_messageCountSemaphore == INVALID_HANDLE_VALUE)
        {
            WriteToInternalErrorsFile(L"Detours Error: Failed opening semaphore for tracking message count - %s\r\n", helperString);
            DWORD error = GetLastError();
            Dbg(L"Failed opening semaphore for tracking message count - Last error: %d, Detours error code: %d\r\n", (int)error, DETOURS_SEMAPHOREOPEN_ERROR_6);
            wprintf(L"Detours Error: Failed opening semaphore for tracking message count - Last error: %d, Detours error code: %d\r\n", (int)error, DETOURS_SEMAPHOREOPEN_ERROR_6);
            fwprintf(stderr, L"Detours Error: Failed opening semaphore for tracking message count - Last error: %d, Detours error code: %d\r\n", (int)error, DETOURS_SEMAPHOREOPEN_ERROR_6);
            HandleDetoursInjectionAndCommunicationErrors(DETOURS_SEMAPHOREOPEN_ERROR_6, L"Detours Error : Failed opening semaphore for tracking message count. exit(-48).", DETOURS_WINDOWS_LOG_MESSAGE_6);
        }

        delete[] helperString;
    }

    PCManifestReport report = reinterpret_cast<PCManifestReport>(&payloadBytes[offset]);
    report->AssertValid();

    if (report->IsReportPresent()) {
        if (report->IsReportHandle()) {
            g_reportFileHandle = g_pDetouredProcessInjector->ReportPipe();
            //g_reportFileHandle = (HANDLE)(intptr_t)(report->Report.ReportHandle32Bit);
#ifdef _DEBUG
#pragma warning( push )
#pragma warning( disable: 4302 4310 4311 4826 )
#if SUPER_VERBOSE
            Dbg(L"report file handle: %llu", (unsigned long long)g_reportFileHandle);
#endif // SUPER_VERBOSE
#pragma warning( pop )
#endif
        }
        else {
            // NOTE: This calls the real CreateFileW(), not our detoured version, because we have not yet installed
            // our detoured functions.
            g_reportFileHandle = CreateFileW(
                report->Report.ReportPath,
                FILE_WRITE_ACCESS,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                NULL,
                OPEN_ALWAYS,
                0,
                NULL);

            if (g_reportFileHandle == INVALID_HANDLE_VALUE) {
                DWORD error = GetLastError();
                g_reportFileHandle = NULL;
                Dbg(L"error: failed to open report file '%s': %08X", report->Report.ReportPath, (int)error);
                wprintf(L"error: failed to open report file '%s': %08X", report->Report.ReportPath, (int)error);
                fwprintf(stderr, L"error: failed to open report file '%s': %08X", report->Report.ReportPath, (int)error);
                HandleDetoursInjectionAndCommunicationErrors(DETOURS_PAYLOAD_PARSE_FAILED_17, L"error: failed to open report file: exit(-59).", DETOURS_WINDOWS_LOG_MESSAGE_17);
                return false;
            }

#if SUPER_VERBOSE
            Dbg(L"report file opened: %s", report->Report.ReportPath);
#endif // SUPER_VERBOSE
        }
    }
    else {
        g_reportFileHandle = NULL;
    }

    offset += report->GetSize();

    PCManifestDllBlock dllBlock = reinterpret_cast<PCManifestDllBlock>(&payloadBytes[offset]);
    dllBlock->AssertValid();

    g_lpDllNameX86 = dllBlock->GetDllString(0);
    g_lpDllNameX64 = dllBlock->GetDllString(1);

    // Update the injector with the DLLs
    g_pDetouredProcessInjector->SetDlls(g_lpDllNameX86, g_lpDllNameX64);
    offset += dllBlock->GetSize();

    PCManifestSubstituteProcessExecutionShim pShimInfo = reinterpret_cast<PCManifestSubstituteProcessExecutionShim>(&payloadBytes[offset]);
    pShimInfo->AssertValid();
    offset += pShimInfo->GetSize();
    g_substituteProcessExecutionShimPath = CreateStringFromWriteChars(payloadBytes, offset);
    if (g_substituteProcessExecutionShimPath != nullptr)
    {
        g_ProcessExecutionShimAllProcesses = pShimInfo->ShimAllProcesses != 0;
        uint32_t numProcessMatches = ParseUint32(payloadBytes, offset);
        g_pShimProcessMatches = new vector<ShimProcessMatch*>();
        for (uint32_t i = 0; i < numProcessMatches; i++)
        {
            wchar_t *processName = CreateStringFromWriteChars(payloadBytes, offset);
            wchar_t *argumentMatch = CreateStringFromWriteChars(payloadBytes, offset);
            g_pShimProcessMatches->push_back(new ShimProcessMatch(processName, argumentMatch));
        }
    }

    g_manifestTreeRoot = reinterpret_cast<PCManifestRecord>(&payloadBytes[offset]);
    VerifyManifestRoot(g_manifestTreeRoot);

    //
    // Try to read module file and check permissions.
    //

    WCHAR wszFileName[MAX_PATH];
    DWORD nFileName = GetModuleFileNameW(NULL, wszFileName, MAX_PATH);
    if (nFileName == 0 || nFileName == MAX_PATH) {
        FileOperationContext fileOperationContextWithoutModuleName(
            L"Process",
            GENERIC_READ,
            FILE_SHARE_READ,
            OPEN_EXISTING,
            0,
            nullptr);

        ReportFileAccess(
            fileOperationContextWithoutModuleName,
            FileAccessStatus_CannotDeterminePolicy,
            PolicyResult(), // Indeterminate
            AccessCheckResult(RequestedAccess::None, ResultAction::Deny, ReportLevel::Report),
            GetLastError(),
            -1);
        return true;
    }

    FileOperationContext fileOperationContext = FileOperationContext::CreateForRead(L"Process", wszFileName);

    PolicyResult policyResult;
    if (!policyResult.Initialize(wszFileName)) {
        policyResult.ReportIndeterminatePolicyAndSetLastError(fileOperationContext);
        return true;
    }

    FileReadContext fileReadContext;
    fileReadContext.FileExistence = FileExistence::Existent; // Clearly this process started somehow.
    fileReadContext.OpenedDirectory = false;
    
    AccessCheckResult readCheck = policyResult.CheckReadAccess(RequestedReadAccess::Read, fileReadContext);

    ReportFileAccess(
        fileOperationContext,
        readCheck.GetFileAccessStatus(),
        policyResult,
        readCheck,
        ERROR_SUCCESS, // No interesting error code to observe or return to anyone.
        -1);

    return true;
}

bool LocateAndParseFileAccessManifest()
{
    const void* manifest;
    DWORD manifestSize;

    if (!LocateFileAccessManifest(/*out*/ manifest, /*out*/ manifestSize)) {
        wprintf(L"Failed to find payload coming from Detours");
        fwprintf(stderr, L"Failed to find payload coming from Detours");
        HandleDetoursInjectionAndCommunicationErrors(DETOURS_NO_PAYLOAD_FOUND_8, L"Failure to find payload coming from Detours: exit(-50).", DETOURS_WINDOWS_LOG_MESSAGE_8);
        return false;
    }

    return ParseFileAccessManifest(manifest, manifestSize);
}

SpecialProcessKind  g_ProcessKind = SpecialProcessKind::NotSpecial;

void InitProcessKind()
{
    struct ProcessPair {
        LPCWSTR Name;
        SpecialProcessKind Kind;
    };

    // This list must be kept in sync with those in C# SandboxedProcessPipExecutor.cs
    const struct ProcessPair pairs[] = {
            { L"csc.exe", SpecialProcessKind::Csc },
            { L"rc.exe", SpecialProcessKind::RC },
            { L"mt.exe", SpecialProcessKind::Mt },
            { L"cvtres.exe", SpecialProcessKind::Cvtres },
            { L"resonexe.exe", SpecialProcessKind::Resonexe},
            { L"windbg.exe", SpecialProcessKind::WinDbg },
            { L"ccrewrite.exe", SpecialProcessKind::CCRewrite },
            { L"cccheck.exe", SpecialProcessKind::CCCheck },
            { L"ccrefgen.exe", SpecialProcessKind::CCRefGen },
            { L"ccdocgen.exe", SpecialProcessKind::CCDocGen } };

    size_t count = sizeof(pairs) / sizeof(pairs[0]);

    WCHAR wszFileName[MAX_PATH];
    DWORD nFileName = GetModuleFileNameW(NULL, wszFileName, MAX_PATH);
    if (nFileName == 0 || nFileName == MAX_PATH) {
        return;
    }

    for (size_t i = 0; i < count; i++) {
        if (HasSuffix(wszFileName, nFileName, pairs[i].Name)) {
            g_ProcessKind = pairs[i].Kind;
            return;
        }
    }
}

void ReportIfNeeded(AccessCheckResult const& checkResult, FileOperationContext const& context, PolicyResult const& policyResult, DWORD error, USN usn, wchar_t const* filter) {
    if (!checkResult.ShouldReport()) {
        return;
    }

    if (checkResult.ShouldDenyAccess()) {
        // Although policyResult may have contained the translated path, TranslateFilePath is called again for debugging purpose.
        std::wstring outFile;
        TranslateFilePath(std::wstring(policyResult.GetCanonicalizedPath().GetPathString()), outFile, true);
    }

    ReportFileAccess(
        context,
        checkResult.GetFileAccessStatus(),
        policyResult,
        checkResult,
        error,
        usn,
        filter);
}

bool EnumerateDirectory(
    const std::wstring& directoryPath,
    const std::wstring& filter,
    bool recursive,
    bool treatReparsePointAsFile,
    _Inout_ std::vector<std::pair<std::wstring, DWORD>>& filesAndDirectories)
{
    HANDLE hFind = INVALID_HANDLE_VALUE;
    WIN32_FIND_DATA ffd;
    std::stack<std::wstring> directoriesToEnumerate;

    directoriesToEnumerate.push(directoryPath);
    filesAndDirectories.clear();

    while (!directoriesToEnumerate.empty()) {
        std::wstring directoryToEnumerate = directoriesToEnumerate.top();
        std::wstring spec = directoryToEnumerate + L"\\" + filter;
        directoriesToEnumerate.pop();

        hFind = FindFirstFileW(spec.c_str(), &ffd);
        if (hFind == INVALID_HANDLE_VALUE) {
            return false;
        }

        do {
            if (wcscmp(ffd.cFileName, L".") != 0 &&
                wcscmp(ffd.cFileName, L"..") != 0) {

                std::wstring path = directoryToEnumerate + L"\\" + ffd.cFileName;
                filesAndDirectories.push_back(std::make_pair(path, ffd.dwFileAttributes));

                if (recursive) {
                    
                    bool isDirectory = (ffd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
                    
                    if (isDirectory && treatReparsePointAsFile) {
                        isDirectory = (ffd.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0;
                    }

                    if (isDirectory) {
                        directoriesToEnumerate.push(path);
                    }
                }
            }
        } while (FindNextFile(hFind, &ffd) != 0);

        if (GetLastError() != ERROR_NO_MORE_FILES) {
            FindClose(hFind);
            return false;
        }

        FindClose(hFind);
        hFind = INVALID_HANDLE_VALUE;
    }

    return true;
}
