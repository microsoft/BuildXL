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

int CallDetouredProcessCreateWithDirectorySymlink()
{
    HMODULE hModule = GetModuleHandleW(NULL);
    WCHAR path[MAX_PATH];
    DWORD nFileName = GetModuleFileNameW(hModule, path, MAX_PATH);

    if (nFileName == 0 || nFileName == MAX_PATH) {
        return ERROR_PATH_NOT_FOUND;
    }

    wstring dirSymlinkPath = L"CreateSymLinkOnDirectories1.dir";
    wstring wpath = wstring(path);
    auto lastSlash = wpath.find_last_of(L"/\\");
    wstring parent = wpath.substr(0, lastSlash);
    wstring fileName = wpath.substr(lastSlash);
    wstring dirSymlinkExePath = dirSymlinkPath + fileName;

    BOOLEAN retCreateSymLink = TestCreateSymbolicLinkW(
        dirSymlinkPath.c_str(),
        parent.c_str(),
        SYMBOLIC_LINK_FLAG_DIRECTORY);

    if (retCreateSymLink == FALSE)
    {
        return (int)GetLastError();
    }

    STARTUPINFO si;
    PROCESS_INFORMATION pi;

    ZeroMemory(&si, sizeof(si));
    si.cb = sizeof(si);
    ZeroMemory(&pi, sizeof(pi));

    std::wstring cmdLine(L"\"");
    cmdLine.append(dirSymlinkExePath.c_str());
    cmdLine.append(L"\" ");
    cmdLine.append(L"CallDetouredCreateFileWWrite");

    if (!CreateProcess(
        NULL,
        &cmdLine[0],
        NULL,
        NULL,
        FALSE,
        0,
        NULL,
        NULL,
        &si,
        &pi))
    {
        return (int)GetLastError();
    }

    // Wait until child process exits.
    WaitForSingleObject(pi.hProcess, INFINITE);

    DWORD childExitCode;
    if (!GetExitCodeProcess(pi.hProcess, &childExitCode))
    {
        return (int)GetLastError();
    }

    if (childExitCode != ERROR_SUCCESS)
    {
        return (int)childExitCode;
    }

    // Close process and thread handles. 
    CloseHandle(pi.hProcess);
    CloseHandle(pi.hThread);
    return (int)GetLastError();
}


int CallDetouredProcessCreateWithSymlink()
{
    HMODULE hModule = GetModuleHandleW(NULL);
    WCHAR path[MAX_PATH];
    DWORD nFileName = GetModuleFileNameW(hModule, path, MAX_PATH);

    if (nFileName == 0 || nFileName == MAX_PATH) {
        return ERROR_PATH_NOT_FOUND;
    }
 
    BOOLEAN retCreateSymLink = TestCreateSymbolicLinkW(
        L"CreateSymbolicLinkTest2.exe",
        path,
        0);

    if (retCreateSymLink == FALSE)
    {
        return (int)GetLastError();
    }

    STARTUPINFO si;
    PROCESS_INFORMATION pi;

    ZeroMemory(&si, sizeof(si));
    si.cb = sizeof(si);
    ZeroMemory(&pi, sizeof(pi));

    std::wstring cmdLine(L"\"");
    cmdLine.append(L"CreateSymbolicLinkTest2.exe");
    cmdLine.append(L"\" ");
    cmdLine.append(L"CallDetouredCreateFileWWrite");

    if (!CreateProcess(
        NULL,
        &cmdLine[0],
        NULL,
        NULL,
        FALSE,
        0,
        NULL,
        NULL,
        &si,
        &pi))
    {
        return (int)GetLastError();
    }

    // Wait until child process exits.
    WaitForSingleObject(pi.hProcess, INFINITE);

    DWORD childExitCode;
    if (!GetExitCodeProcess(pi.hProcess, &childExitCode))
    {
        return (int)GetLastError();
    }

    if (childExitCode != ERROR_SUCCESS)
    {
        return (int)childExitCode;
    }

    // Close process and thread handles. 
    CloseHandle(pi.hProcess);
    CloseHandle(pi.hThread);
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

int CallDetouredFileCreateOnSymlink(bool openWithReparsePoint)
{
    HANDLE hFile = CreateFileW(
        L"SourceOfSymLink.link",
        GENERIC_READ,
        FILE_SHARE_READ,
        0,
        OPEN_EXISTING,
        (DWORD)(openWithReparsePoint ? FILE_FLAG_OPEN_REPARSE_POINT : FILE_ATTRIBUTE_NORMAL),
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
    return CallDetouredFileCreateOnSymlink(false);
}

int CallDetouredFileCreateThatDoesNotAccessChainOfSymlinks()
{
    return CallDetouredFileCreateOnSymlink(true);
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

int CallDetouredCopyFileToExistingSymlink(bool copySymlink)
{
    if (!TestCreateSymbolicLinkW(L"LinkToDestination.link", L"Destination.txt", 0))
    {
        return (int)GetLastError();
    }

    CopyFileExW(
        L"LinkToSource.link",
        L"LinkToDestination.link",
        (LPPROGRESS_ROUTINE)NULL,
        (LPVOID)NULL,
        (LPBOOL)NULL,
        copySymlink ? COPY_FILE_COPY_SYMLINK : (DWORD)0x0);

    return (int)GetLastError();
}
int CallDetouredCopyFileToExistingSymlinkFollowChainOfSymlinks()
{
    return CallDetouredCopyFileToExistingSymlink(false);
}

int CallDetouredCopyFileToExistingSymlinkNotFollowChainOfSymlinks()
{
    return CallDetouredCopyFileToExistingSymlink(true);
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

int CallDetouredNtCreateFileOnSymlink(bool withReparsePointFlag)
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
        FILE_NON_DIRECTORY_FILE | (DWORD)(withReparsePointFlag ? FILE_OPEN_REPARSE_POINT : 0),
        NULL,
        NULL);

    if (hFile != INVALID_HANDLE_VALUE)
    {
        NtClose(hFile);
    }

    return (int)RtlNtStatusToDosError(status);
}

int CallDetouredNtCreateFileThatAccessesChainOfSymlinks()
{
    return CallDetouredNtCreateFileOnSymlink(false);
}

int CallDetouredNtCreateFileThatDoesNotAccessChainOfSymlinks()
{
    return CallDetouredNtCreateFileOnSymlink(true);
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

int CallProbeDirectorySymlink()
{
    DWORD attributes = GetFileAttributesW(L"directory.lnk");
    if ((attributes & FILE_ATTRIBUTE_DIRECTORY) == 0)
    {
        return -1;
    }

    return (int)GetLastError();
}

int CallProbeDirectorySymlinkTarget(bool withReparsePointFlag)
{
    DWORD flagsAndAttributes = FILE_FLAG_BACKUP_SEMANTICS;
    flagsAndAttributes = withReparsePointFlag
        ? flagsAndAttributes | FILE_FLAG_OPEN_REPARSE_POINT
        : flagsAndAttributes;

    HANDLE hFile = CreateFileW(
        L"directory.lnk",
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
    return (int)GetLastError();
}

int CallProbeDirectorySymlinkTargetWithReparsePointFlag()
{
    return CallProbeDirectorySymlinkTarget(true);
}

int CallProbeDirectorySymlinkTargetWithoutReparsePointFlag()
{
    return CallProbeDirectorySymlinkTarget(false);
}

int CallValidateFileSymlinkAccesses()
{
    HANDLE hFile = CreateFileW(
        L"AnotherDirectory\\Target_DirectorySymlink\\symlink.txt",
        GENERIC_READ | GENERIC_WRITE,
        FILE_SHARE_READ,
        0,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }

    std::string content = "Some content to write";
    DWORD bytes_written;

    // Write content throught the symbolic file link
    WriteFile(hFile, content.c_str(), (DWORD)content.size(), &bytes_written, nullptr);
    CloseHandle(hFile);

    return (int)GetLastError();
}

int CallOpenFileThroughMultipleDirectorySymlinks()
{
    HANDLE hFile = CreateFileW(
        L"A\\B.lnk\\C\\D.lnk\\e.txt",
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

int CallOpenFileThroughDirectorySymlinksSelectivelyEnforce()
{
    HANDLE hFile = CreateFileW(
        L"F\\A.lnk\\D\\B.lnk\\e.txt",
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

int CallModifyDirectorySymlinkThroughDifferentPathIgnoreFullyResolve()
{
    HANDLE hFile = CreateFileW(
        L"DD.lnk\\f.lnk",
        GENERIC_READ,
        FILE_SHARE_READ | FILE_SHARE_DELETE | FILE_SHARE_WRITE,
        NULL,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }

    CloseHandle(hFile);

    // Invalidate directory symlink
    if (!RemoveDirectoryW(L"D.lnk"))
    {
        return (int)GetLastError();
    }

    // Recreate the symbolic link chain
    if (!TestCreateSymbolicLinkW(L"D.lnk", L"D2", SYMBOLIC_LINK_FLAG_DIRECTORY))
    {
        return (int)GetLastError();
    }

    hFile = CreateFileW(
        L"DD.lnk\\f.lnk",
        GENERIC_READ,
        FILE_SHARE_READ | FILE_SHARE_DELETE | FILE_SHARE_WRITE,
        NULL,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }

    CloseHandle(hFile);
    return 0;
}
