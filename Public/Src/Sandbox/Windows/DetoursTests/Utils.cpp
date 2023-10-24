// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// Utils.cpp : Defines the utilities needed for the tests.

#include "stdafx.h"

#include "Utils.h"

// warning C6387: 'GetModuleHandleW(L"ntdll.dll")' could be '0'.
// warning C26471: Don't use reinterpret_cast. A cast from void* can use static_cast (type.1).
// warning C26474: Don't cast between pointer types when the conversion could be implicit (type.1).
// warning C26485: Expression 'buffer': No array to pointer decay (bounds.3).
// warning C26414: Move, copy, reassign or reset a local smart pointer 'buffer' (r.5).
// warning C26490: Don't use reinterpret_cast (type.1).
// warning C26472: Don't use a static_cast for arithmetic conversions. Use brace initialization, gsl::narrow_cast or gsl::narrow (type.1).
// warning C26812: The enum type 'FILE_INFORMATION_CLASS' is unscoped. Prefer 'enum class' over 'enum' (Enum.3).
#pragma warning( disable : 4061 6387 26471 26474 26485 26414 26490 26472 26812 )

using namespace std;

_NtCreateFile GetNtCreateFile()
{
    return reinterpret_cast<_NtCreateFile>(reinterpret_cast<void*>(GetProcAddress(GetModuleHandle(L"ntdll.dll"), "NtCreateFile")));
}

_NtClose GetNtClose()
{
    return reinterpret_cast<_NtClose>(reinterpret_cast<void*>(GetProcAddress(GetModuleHandle(L"ntdll.dll"), "NtClose")));
}

_RtlInitUnicodeString GetRtlInitUnicodeString()
{
    return reinterpret_cast<_RtlInitUnicodeString>(reinterpret_cast<void*>(GetProcAddress(GetModuleHandle(L"ntdll.dll"), "RtlInitUnicodeString")));
}

bool TryGetFullPath(_In_ LPCWSTR path, _Inout_ wstring& fullPath)
{
    constexpr int BufferSize = 4096;
    WCHAR buffer[BufferSize] = L"";
    WCHAR** lppPart = { NULL };

    const DWORD result = GetFullPathNameW(
        path,
        BufferSize,
        buffer,
        lppPart);

    if (result >= BufferSize)
    {
        wprintf(L"TryGetFullPath: Buffer size for '%s' is not enough. Required size is %lu \n", path, result);
        return false;
    }

    if (result != 0)
    {
        fullPath.append(buffer);
        return true;
    }

    wprintf(L"TryGetFullPath: Failed GetFullPathNameW: error: %lu \n", GetLastError());
    return false;
}

bool TryGetNtFullPath(_In_ LPCWSTR path, _Out_ wstring& fullPath)
{
    fullPath.append(L"\\??\\");
    return TryGetFullPath(path, fullPath);
}

bool TryGetNtEscapedFullPath(_In_ LPCWSTR path, _Out_ wstring& fullPath)
{
    fullPath.append(L"\\\\?\\");
    return TryGetFullPath(path, fullPath);
}

BOOLEAN TestCreateSymbolicLinkW(_In_ LPCWSTR lpSymlinkFileName, _In_ LPCWSTR lpTargetFileName, _In_ DWORD dwFlags)
{
    BOOLEAN res = CreateSymbolicLinkW(lpSymlinkFileName, lpTargetFileName, dwFlags | 0x2);
    const DWORD lastError = GetLastError();
    
    if (lastError == ERROR_INVALID_PARAMETER)
    {
        res = CreateSymbolicLinkW(lpSymlinkFileName, lpTargetFileName, dwFlags);
    }

    return res;
}

BOOLEAN TestCreateSymbolicLinkA(_In_ LPCSTR lpSymlinkFileName, _In_ LPCSTR lpTargetFileName, _In_ DWORD dwFlags)
{
    BOOLEAN res = CreateSymbolicLinkA(lpSymlinkFileName, lpTargetFileName, dwFlags | 0x2);
    const DWORD lastError = GetLastError();

    if (lastError == ERROR_INVALID_PARAMETER)
    {
        res = CreateSymbolicLinkA(lpSymlinkFileName, lpTargetFileName, dwFlags);
    }

    return res;
}

BOOL SetRenameFileByHandle(HANDLE hFile, const wstring& target, bool correctFileNameLength)
{
    const size_t targetLength = target.length();
    const size_t targetLengthInBytes = targetLength * sizeof(WCHAR);
    const size_t bufferSize = sizeof(FILE_RENAME_INFO) + targetLengthInBytes;
    auto const buffer = make_unique<char[]>(bufferSize);
    auto const fri = reinterpret_cast<PFILE_RENAME_INFO>(buffer.get());
    fri->ReplaceIfExists = TRUE;
    fri->FileNameLength = correctFileNameLength ? static_cast<ULONG>(targetLengthInBytes) : static_cast<ULONG>(targetLength);
    fri->RootDirectory = nullptr;
    wmemcpy(fri->FileName, target.c_str(), targetLength + 1);

    return SetFileInformationByHandle(
        hFile,
        FILE_INFO_BY_HANDLE_CLASS::FileRenameInfo,
        fri,
        static_cast<DWORD>(bufferSize));
}

NTSTATUS ZwSetRenameFileByHandle(HANDLE hFile, LPCWSTR targetName, FILE_INFORMATION_CLASS_EXTRA fileInfoClass)
{
    assert(fileInfoClass == FILE_INFORMATION_CLASS_EXTRA::FileRenameInformation
        || fileInfoClass == FILE_INFORMATION_CLASS_EXTRA::FileRenameInformationEx
        || fileInfoClass == FILE_INFORMATION_CLASS_EXTRA::FileRenameInformationBypassAccessCheck
        || fileInfoClass == FILE_INFORMATION_CLASS_EXTRA::FileRenameInformationExBypassAccessCheck);

    wstring target = wstring();
    if (!TryGetNtFullPath(targetName, target))
    {
        return static_cast<NTSTATUS>(STATUS_INVALID_HANDLE);
    }

    const size_t targetLength = target.length();
    const size_t targetLengthInBytes = targetLength * sizeof(WCHAR);
    const size_t bufferSize = sizeof(FILE_RENAME_INFO) + targetLengthInBytes;
    auto const buffer = make_unique<char[]>(bufferSize);
    auto const fri = reinterpret_cast<PFILE_RENAME_INFO>(buffer.get());
    fri->ReplaceIfExists = TRUE;
    fri->FileNameLength = static_cast<ULONG>(targetLengthInBytes);
    fri->RootDirectory = nullptr;
    wmemcpy(fri->FileName, target.c_str(), targetLength);

    IO_STATUS_BLOCK ioStatusBlock{};
    return ZwSetInformationFile(
        hFile,
        &ioStatusBlock,
        fri,
        static_cast<ULONG>(bufferSize),
        static_cast<FILE_INFORMATION_CLASS>(FILE_INFORMATION_CLASS_EXTRA::FileRenameInformation));
}

BOOL SetFileDispositionByHandle(HANDLE hFile, FILE_INFO_BY_HANDLE_CLASS fileInfoClass)
{
    LPVOID fileInfo = NULL;
    FILE_DISPOSITION_INFO fi{};
    FILE_DISPOSITION_INFO_EX fiEx{};
    size_t bufferSize = 0;

    switch (fileInfoClass)
    {
        case FILE_INFO_BY_HANDLE_CLASS::FileDispositionInfo:
            bufferSize = sizeof(FILE_DISPOSITION_INFO);
            fi.DeleteFile = TRUE;
            fileInfo = &fi;
            break;
        case FILE_INFO_BY_HANDLE_CLASS::FileDispositionInfoEx:
            bufferSize = sizeof(FILE_DISPOSITION_INFO_EX);
            fiEx.Flags = FILE_DISPOSITION_FLAG_DELETE;
            fileInfo = &fiEx;
            break;
        default:
            assert(FALSE);
            break;
    }

    return SetFileInformationByHandle(
        hFile,
        fileInfoClass,
        fileInfo,
        static_cast<DWORD>(bufferSize));
}

NTSTATUS ZwSetFileDispositionByHandle(HANDLE hFile, FILE_INFORMATION_CLASS_EXTRA fileInfoClass)
{
    PVOID fileInfo = NULL;
    FILE_DISPOSITION_INFO fi{};
    FILE_DISPOSITION_INFO_EX fiEx{};
    size_t bufferSize = 0;

    switch (fileInfoClass)
    {
        case FILE_INFORMATION_CLASS_EXTRA::FileDispositionInformation:
            bufferSize = sizeof(FILE_DISPOSITION_INFO);
            fi.DeleteFile = TRUE;
            fileInfo = &fi;
            break;
        case FILE_INFORMATION_CLASS_EXTRA::FileDispositionInformationEx:
            bufferSize = sizeof(FILE_DISPOSITION_INFO_EX);
            fiEx.Flags = FILE_DISPOSITION_FLAG_DELETE;
            fileInfo = &fiEx;
            break;
        default:
            assert(FALSE);
            break;
    }

    IO_STATUS_BLOCK ioStatusBlock = { 0 };
    return ZwSetInformationFile(
        hFile,
        &ioStatusBlock,
        fileInfo,
        (ULONG)bufferSize,
        (FILE_INFORMATION_CLASS)fileInfoClass);
}

NTSTATUS OpenFileWithNtCreateFile(
    PHANDLE FileHandle,
    LPCWSTR path,
    HANDLE rootDirectory,
    ACCESS_MASK DesiredAccess,
    ULONG FileAttributes,
    ULONG ShareAccess,
    ULONG CreateDisposition,
    ULONG CreateOptions)
{
    _NtCreateFile NtCreateFile = GetNtCreateFile();
    _RtlInitUnicodeString RtlInitUnicodeString = GetRtlInitUnicodeString();

    assert(NtCreateFile != nullptr);
    assert(RtlInitUnicodeString != nullptr);

    OBJECT_ATTRIBUTES objAttribs = { 0 };

    UNICODE_STRING unicodeString{};
    RtlInitUnicodeString(&unicodeString, path);

    InitializeObjectAttributes(&objAttribs, &unicodeString, OBJ_CASE_INSENSITIVE, rootDirectory, NULL);

    constexpr int allocSize = 2048;
    LARGE_INTEGER largeInteger = { { 0 } };
    largeInteger.QuadPart = allocSize;

    IO_STATUS_BLOCK ioStatusBlock = { 0 };
    const NTSTATUS status = NtCreateFile(
        FileHandle,
        DesiredAccess,
        &objAttribs,
        &ioStatusBlock,
        &largeInteger,
        FileAttributes,
        ShareAccess,
        CreateDisposition,
        CreateOptions,
        NULL,
        NULL);

    return status;
}
