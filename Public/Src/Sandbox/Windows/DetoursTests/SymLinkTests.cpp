// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "stdafx.h"

#include <windows.h>
#include <winternl.h>
#include <tchar.h>
#include <stdio.h>
#include <strsafe.h>
#include <cstdio>

#include "SymLinkTests.h"
#include "Utils.h"

int CallAccessSymLinkOnDirectories()
{
    HANDLE hFile = CreateFileW(
        L"input\\AccessSymLinkOnDirectories1.dir\\foo.txt",
        GENERIC_READ,
        FILE_SHARE_READ,
        0,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return  (int)GetLastError();
    }

    CloseHandle(hFile);

    return (int)GetLastError();
}

int CallAccessSymLinkOnFiles()
{
    HANDLE hFile = CreateFileW(
        L"input\\AccessSymLinkOnFiles1.txt", // This is a symlink.
        GENERIC_READ,
        FILE_SHARE_READ,
        0,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }

    wchar_t buff[10];
    DWORD read = 0;

    BOOL ret1 = ReadFile(hFile, (void*)buff, 3, &read, nullptr);
    if (!ret1)
    {
        CloseHandle(hFile);
        return (int)GetLastError();
    }

    if (!wcsncmp(buff, L"aaa", 3))
    {
        CloseHandle(hFile);
        return 99;
    }

    CloseHandle(hFile);

    return (int)GetLastError();
}

int CallCreateSymLinkOnFiles()
{
    TestCreateSymbolicLinkW(
        L"input\\CreateSymLinkOnFiles1.txt",
        L"input\\CreateSymLinkOnFiles2.txt",
        0);
    
    return (int)GetLastError();
}

int CallDetouredAccessesCreateSymlinkForQBuild()
{
    TestCreateSymbolicLinkW(
        L"input\\CreateSymbolicLinkTest1.txt",
        L"input\\CreateSymbolicLinkTest2.txt",
        0);

    return (int)GetLastError();;
}

int CallCreateAndDeleteSymLinkOnFiles()
{
    // Create symlink.
    TestCreateSymbolicLinkW(
        L"input\\SymlinkToIrrelevantExistingFile.lnk",
        L"input\\IrrelevantExistingFile.txt",
        0);

    // Delete symlink.
    DeleteFileW(L"input\\SymlinkToIrrelevantExistingFile.lnk");

    // Recreate symlink.
    TestCreateSymbolicLinkW(
        L"input\\SymlinkToIrrelevantExistingFile.lnk",
        L"input\\IrrelevantExistingFile.txt",
        0);

    return (int)GetLastError();
}

int CallMoveSymLinkOnFilesNotEnforceChainSymLinkAccesses()
{
    // Implicitly MoveFileW => MoveFileWithProgress(a, b, NULL, NULL, MOVEFILE_COPY_ALLOWED)
    MoveFileW(L"OldSymlinkToIrrelevantExistingFile.lnk", L"NewSymlinkToIrrelevantExistingFile.lnk");

    return (int)GetLastError();
}

int CallAccessOnChainOfJunctions()
{
    // Open the junction to exercise the policy enforcement on junction.
    HANDLE hJunction = CreateFileW(
        L"SourceOfSymLink.link",
        GENERIC_READ,
        FILE_SHARE_READ,
        0,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_BACKUP_SEMANTICS,
        NULL);

    if (hJunction == INVALID_HANDLE_VALUE)
    {
        return  (int)GetLastError();
    }

    CloseHandle(hJunction);

    HANDLE hFile = CreateFileW(
        L"SourceOfSymLink.link\\Target.txt",
        GENERIC_READ,
        FILE_SHARE_READ,
        0,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return  (int)GetLastError();
    }

    CloseHandle(hFile);

    return (int)GetLastError();
}

int CallCreateSymLinkOnDirectories()
{
    TestCreateSymbolicLinkW(
        L"input\\CreateSymLinkOnDirectories1.dir",
        L"input\\CreateSymLinkOnDirectories2.dir",
        SYMBOLIC_LINK_FLAG_DIRECTORY);

    return (int)GetLastError();
}

int CallDetouredFileCreateWithSymlink()
{
    HANDLE hFile = CreateFileW(
        L"input\\CreateSymbolicLinkTest2.txt",
        GENERIC_READ,
        FILE_SHARE_READ,
        0,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE) 
    {
        return (int)GetLastError();
    }

    CloseHandle(hFile);

    BOOLEAN retCreateSymLink = TestCreateSymbolicLinkW(
        L"input\\CreateSymbolicLinkTest1.txt",
        L"input\\CreateSymbolicLinkTest2.txt",
        0);

    if (retCreateSymLink == FALSE)
    {
        return (int)GetLastError();
    }

    hFile = CreateFileW(
        L"input\\CreateSymbolicLinkTest1.txt",
        GENERIC_READ,
        FILE_SHARE_READ,
        0,
        OPEN_EXISTING,
        FILE_FLAG_OPEN_REPARSE_POINT,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE) 
    {
        return (int)GetLastError();
    }

    return (int)GetLastError();
}

int CallDetouredFileCreateWithNoSymlink()
{
    HANDLE hFile = CreateFileW(
        L"input\\CreateNoSymbolicLinkTest1.txt",
        GENERIC_WRITE,
        FILE_SHARE_READ,
        0,
        CREATE_NEW,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }

    CloseHandle(hFile);

    hFile = CreateFileW(
        L"input\\CreateNoSymbolicLinkTest2.txt",
        GENERIC_READ,
        FILE_SHARE_READ,
        0,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }

    CloseHandle(hFile);

    return (int)GetLastError();
}

int CallDetouredFileCreateThatAccessesChainOfSymlinks()
{
    HANDLE hFile = CreateFileW(
        L"SourceOfSymLink.link",
        GENERIC_READ,
        FILE_SHARE_READ,
        0,
        OPEN_EXISTING,
        FILE_FLAG_OPEN_REPARSE_POINT,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }

    CloseHandle(hFile);
    
    return (int)GetLastError();
}

int CallDetouredCopyFileFollowingChainOfSymlinks()
{
    CopyFileW(
        L"SourceOfSymLink.link",
        L"CopiedFile.txt",
        FALSE);

    return (int)GetLastError();
}

int CallDetouredCopyFileNotFollowingChainOfSymlinks()
{
    CopyFileExW(
        L"SourceOfSymLink.link",
        L"CopiedFile.txt",
        (LPPROGRESS_ROUTINE)NULL,
        (LPVOID)NULL,
        (LPBOOL)NULL,
        COPY_FILE_COPY_SYMLINK);

    return (int)GetLastError();
}

int CallAccessNestedSiblingSymLinkOnFiles()
{
    HANDLE hFile = CreateFileW(
        L"imports\\x64\\symlink.imports.link", // This is a symlink.
        GENERIC_READ,
        FILE_SHARE_READ | FILE_SHARE_DELETE | FILE_SHARE_WRITE,
        NULL,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_BACKUP_SEMANTICS,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }

    wchar_t buff[10];
    DWORD read = 0;

    BOOL ret1 = ReadFile(hFile, (void*)buff, 3, &read, nullptr);
    if (!ret1)
    {
        CloseHandle(hFile);
        return (int)GetLastError();
    }

    if (!wcsncmp(buff, L"aaa", 3))
    {
        CloseHandle(hFile);
        return 99;
    }

    CloseHandle(hFile);

    return (int)GetLastError();
}

int CallAccessJunctionSymlink_Real()
{
    HANDLE hFile = CreateFileW(
        L"real\\subdir\\symlink.link", // This is a symlink.
        GENERIC_READ,
        FILE_SHARE_READ,
        0,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }

    wchar_t buff[10];
    DWORD read = 0;

    BOOL ret1 = ReadFile(hFile, (void*)buff, 4, &read, nullptr);
    if (!ret1)
    {
        CloseHandle(hFile);
        return (int)GetLastError();
    }

    if (!wcsncmp(buff, L"real", 4))
    {
        CloseHandle(hFile);
        return 99;
    }

    CloseHandle(hFile);

    return (int)GetLastError();
}

int CallAccessJunctionSymlink_Junction()
{
    HANDLE hFile = CreateFileW(
        L"junction\\subdir\\symlink.link", // This is a symlink.
        GENERIC_READ,
        FILE_SHARE_READ,
        0,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }

    wchar_t buff[10];
    DWORD read = 0;

    BOOL ret1 = ReadFile(hFile, (void*)buff, 8, &read, nullptr);
    if (!ret1)
    {
        CloseHandle(hFile);
        return (int)GetLastError();
    }

    if (!wcsncmp(buff, L"junction", 8))
    {
        CloseHandle(hFile);
        return 99;
    }

    CloseHandle(hFile);

    return (int)GetLastError();
}

int CallDetouredNtCreateFileThatAccessesChainOfSymlinks()
{
    _NtCreateFile NtCreateFile = GetNtCreateFile();
    _NtClose NtClose = GetNtClose();
    _RtlInitUnicodeString RtlInitUnicodeString = GetRtlInitUnicodeString();

    HANDLE hFile;
    OBJECT_ATTRIBUTES objAttribs = { 0 };

    wstring fullPath;
    if (!TryGetNtFullPath(L"SourceOfSymLink.link", fullPath))
    {
        return (int)GetLastError();
    }

    UNICODE_STRING unicodeString;
    RtlInitUnicodeString(&unicodeString, fullPath.c_str());

    InitializeObjectAttributes(&objAttribs, &unicodeString, OBJ_CASE_INSENSITIVE, NULL, NULL);

    const int allocSize = 2048;
    LARGE_INTEGER largeInteger = { 0 };
    largeInteger.QuadPart = allocSize;

    IO_STATUS_BLOCK ioStatusBlock = { 0 };
    NTSTATUS status = NtCreateFile(
        &hFile, 
        FILE_GENERIC_READ,
        &objAttribs, 
        &ioStatusBlock, 
        &largeInteger,
        FILE_ATTRIBUTE_NORMAL, 
        FILE_SHARE_READ, 
        FILE_OPEN, 
        FILE_NON_DIRECTORY_FILE | FILE_OPEN_REPARSE_POINT,
        NULL, 
        NULL);

    if (hFile != INVALID_HANDLE_VALUE)
    {
        NtClose(hFile);
    }

    return (int)RtlNtStatusToDosError(status);
}

int CallDetouredCreateFileWForSymlinkProbeOnly(bool withReparsePointFlag)
{
    DWORD flagsAndAttributes = FILE_FLAG_BACKUP_SEMANTICS;
    flagsAndAttributes = withReparsePointFlag
        ? flagsAndAttributes | FILE_FLAG_OPEN_REPARSE_POINT
        : flagsAndAttributes;

    HANDLE hFile = CreateFileW(
        L"input\\CreateFileWForProbingOnly.lnk",
        0,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        NULL,
        OPEN_EXISTING,
        flagsAndAttributes,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }

    CloseHandle(hFile);

    hFile = CreateFileW(
        L"input\\CreateFileWForProbingOnly.lnk",
        FILE_READ_ATTRIBUTES | FILE_READ_EA,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        NULL,
        OPEN_EXISTING,
        flagsAndAttributes,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }

    CloseHandle(hFile);

    return (int)GetLastError();
}

int CallDetouredCreateFileWForSymlinkProbeOnlyWithReparsePointFlag()
{
    return CallDetouredCreateFileWForSymlinkProbeOnly(true);
}

int CallDetouredCreateFileWForSymlinkProbeOnlyWithoutReparsePointFlag()
{
    return CallDetouredCreateFileWForSymlinkProbeOnly(false);
}