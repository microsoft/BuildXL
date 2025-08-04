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

// warning C26472: Don't use a static_cast for arithmetic conversions. Use brace initialization, gsl::narrow_cast or gsl::narrow (type.1).
// warning C26485: Expression 'buff': No array to pointer decay (bounds.3).
// warning C26446: Prefer to use gsl::at() instead of unchecked subscript operator (bounds.4).
// warning C26482: Only index into arrays using constant expressions (bounds.2).
// warning C26481: Don't use pointer arithmetic. Use span instead (bounds.1).
#pragma warning( disable : 26472 26485 26446 26482 26481 )

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
        return  static_cast<int>(GetLastError());
    }

    CloseHandle(hFile);

    return static_cast<int>(GetLastError());
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
        return static_cast<int>(GetLastError());
    }

    wchar_t buff[10] = { 0 };
    DWORD read = 0;

    const BOOL ret1 = ReadFile(hFile, (void*)buff, 3, &read, nullptr);
    if (!ret1)
    {
        CloseHandle(hFile);
        return static_cast<int>(GetLastError());
    }

    buff[read] = L'\0';

    if (!wcsncmp(buff, L"aaa", 3))
    {
        CloseHandle(hFile);
        return 99;
    }

    CloseHandle(hFile);

    return static_cast<int>(GetLastError());
}

int CallCreateSymLinkOnFiles()
{
    TestCreateSymbolicLinkW(
        L"input\\CreateSymLinkOnFiles1.txt",
        L"input\\CreateSymLinkOnFiles2.txt",
        0);
    
    return static_cast<int>(GetLastError());
}

int CallDetouredAccessesCreateSymlinkForQBuild()
{
    TestCreateSymbolicLinkW(
        L"input\\CreateSymbolicLinkTest1.txt",
        L"input\\CreateSymbolicLinkTest2.txt",
        0);

    return static_cast<int>(GetLastError());;
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

    return static_cast<int>(GetLastError());
}

int CallMoveSymLinkOnFilesNotEnforceChainSymLinkAccesses()
{
    // Implicitly MoveFileW => MoveFileWithProgress(a, b, NULL, NULL, MOVEFILE_COPY_ALLOWED)
    MoveFileW(L"OldSymlinkToIrrelevantExistingFile.lnk", L"NewSymlinkToIrrelevantExistingFile.lnk");

    return static_cast<int>(GetLastError());
}

int CallAccessOnChainOfJunctions()
{
    // Probe the junction without reparse point flag to exercise the policy enforcement on junction.
    HANDLE hJunction = CreateFileW(
        L"SourceJunction",
        0,
        FILE_SHARE_READ,
        0,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_BACKUP_SEMANTICS,
        NULL);

    if (hJunction == INVALID_HANDLE_VALUE)
    {
        return  static_cast<int>(GetLastError());
    }

    CloseHandle(hJunction);

    HANDLE hFile = CreateFileW(
        L"SourceJunction\\target.txt",
        GENERIC_READ,
        FILE_SHARE_READ,
        0,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return  static_cast<int>(GetLastError());
    }

    CloseHandle(hFile);

    return static_cast<int>(GetLastError());
}

int CallCreateSymLinkOnDirectories()
{
    TestCreateSymbolicLinkW(
        L"input\\CreateSymLinkOnDirectories1.dir",
        L"input\\CreateSymLinkOnDirectories2.dir",
        SYMBOLIC_LINK_FLAG_DIRECTORY);

    return static_cast<int>(GetLastError());
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
        return static_cast<int>(GetLastError());
    }

    CloseHandle(hFile);

    const BOOLEAN retCreateSymLink = TestCreateSymbolicLinkW(
        L"input\\CreateSymbolicLinkTest1.txt",
        L"input\\CreateSymbolicLinkTest2.txt",
        0);

    if (retCreateSymLink == FALSE)
    {
        return static_cast<int>(GetLastError());
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
        return static_cast<int>(GetLastError());
    }

    return static_cast<int>(GetLastError());
}

int CallDetouredProcessCreateWithDirectorySymlink()
{
    HMODULE hModule = GetModuleHandleW(NULL);
    WCHAR path[MAX_PATH];
    const DWORD nFileName = GetModuleFileNameW(hModule, path, MAX_PATH);

    if (nFileName == 0 || nFileName == MAX_PATH) {
        return ERROR_PATH_NOT_FOUND;
    }

    const wstring dirSymlinkPath = L"CreateSymLinkOnDirectories1.dir";
    const wstring wpath = wstring(path);
    const auto lastSlash = wpath.find_last_of(L"/\\");
    const wstring parent = wpath.substr(0, lastSlash);
    const wstring fileName = wpath.substr(lastSlash);
    const wstring dirSymlinkExePath = dirSymlinkPath + fileName;

    const BOOLEAN retCreateSymLink = TestCreateSymbolicLinkW(
        dirSymlinkPath.c_str(),
        parent.c_str(),
        SYMBOLIC_LINK_FLAG_DIRECTORY);

    if (retCreateSymLink == FALSE)
    {
        return static_cast<int>(GetLastError());
    }

    STARTUPINFO si{};
    PROCESS_INFORMATION pi{};

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
        return static_cast<int>(GetLastError());
    }

    // Wait until child process exits.
    WaitForSingleObject(pi.hProcess, INFINITE);

    DWORD childExitCode = 0;
    if (!GetExitCodeProcess(pi.hProcess, &childExitCode))
    {
        return static_cast<int>(GetLastError());
    }

    if (childExitCode != ERROR_SUCCESS)
    {
        return static_cast<int>(childExitCode);
    }

    // Close process and thread handles. 
    CloseHandle(pi.hProcess);
    CloseHandle(pi.hThread);
    return static_cast<int>(GetLastError());
}


int CallDetouredProcessCreateWithSymlink()
{
    HMODULE hModule = GetModuleHandleW(NULL);
    WCHAR path[MAX_PATH];
    const DWORD nFileName = GetModuleFileNameW(hModule, path, MAX_PATH);

    if (nFileName == 0 || nFileName == MAX_PATH) {
        return ERROR_PATH_NOT_FOUND;
    }
 
    const BOOLEAN retCreateSymLink = TestCreateSymbolicLinkW(
        L"CreateSymbolicLinkTest2.exe",
        path,
        0);

    if (retCreateSymLink == FALSE)
    {
        return static_cast<int>(GetLastError());
    }

    STARTUPINFO si{};
    PROCESS_INFORMATION pi{};

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
        return static_cast<int>(GetLastError());
    }

    // Wait until child process exits.
    WaitForSingleObject(pi.hProcess, INFINITE);

    DWORD childExitCode = 0;
    if (!GetExitCodeProcess(pi.hProcess, &childExitCode))
    {
        return static_cast<int>(GetLastError());
    }

    if (childExitCode != ERROR_SUCCESS)
    {
        return static_cast<int>(childExitCode);
    }

    // Close process and thread handles. 
    CloseHandle(pi.hProcess);
    CloseHandle(pi.hThread);
    return static_cast<int>(GetLastError());
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
        return static_cast<int>(GetLastError());
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
        return static_cast<int>(GetLastError());
    }

    CloseHandle(hFile);

    return static_cast<int>(GetLastError());
}

int CallDetouredFileCreateOnSymlink(bool openWithReparsePoint)
{
    HANDLE hFile = CreateFileW(
        L"SourceOfSymLink.link",
        GENERIC_READ,
        FILE_SHARE_READ,
        0,
        OPEN_EXISTING,
        static_cast<DWORD>(openWithReparsePoint ? FILE_FLAG_OPEN_REPARSE_POINT : FILE_ATTRIBUTE_NORMAL),
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return static_cast<int>(GetLastError());
    }

    CloseHandle(hFile);

    return static_cast<int>(GetLastError());
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

    return static_cast<int>(GetLastError());
}

int CallDetouredCopyFileNotFollowingChainOfSymlinks()
{
    CopyFileExW(
        L"SourceOfSymLink.link",
        L"CopiedFile.txt",
        static_cast<LPPROGRESS_ROUTINE>(NULL),
        static_cast<LPVOID>(NULL),
        static_cast<LPBOOL>(NULL),
        COPY_FILE_COPY_SYMLINK);

    return static_cast<int>(GetLastError());
}

int CallDetouredCopyFileToExistingSymlink(bool copySymlink)
{
    if (!TestCreateSymbolicLinkW(L"LinkToDestination.link", L"Destination.txt", 0))
    {
        return static_cast<int>(GetLastError());
    }

    CopyFileExW(
        L"LinkToSource.link",
        L"LinkToDestination.link",
        static_cast<LPPROGRESS_ROUTINE>(NULL),
        static_cast<LPVOID>(NULL),
        static_cast<LPBOOL>(NULL),
        copySymlink ? COPY_FILE_COPY_SYMLINK : (DWORD)0x0);

    return static_cast<int>(GetLastError());
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
        return static_cast<int>(GetLastError());
    }

    wchar_t buff[10] = { 0 };
    DWORD read = 0;

    const BOOL ret1 = ReadFile(hFile, (void*)buff, 3, &read, nullptr);
    if (!ret1)
    {
        CloseHandle(hFile);
        return static_cast<int>(GetLastError());
    }

    buff[read] = L'\0';

    if (!wcsncmp(buff, L"aaa", 3))
    {
        CloseHandle(hFile);
        return 99;
    }

    CloseHandle(hFile);

    return static_cast<int>(GetLastError());
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
        return static_cast<int>(GetLastError());
    }

    wchar_t buff[10] = { 0 };
    DWORD read = 0;

    const BOOL ret1 = ReadFile(hFile, (void*)buff, 4, &read, nullptr);
    if (!ret1)
    {
        CloseHandle(hFile);
        return static_cast<int>(GetLastError());
    }

    buff[read] = L'\0';

    if (!wcsncmp(buff, L"real", 4))
    {
        CloseHandle(hFile);
        return 99;
    }

    CloseHandle(hFile);

    return static_cast<int>(GetLastError());
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
        return static_cast<int>(GetLastError());
    }

    wchar_t buff[10] = { 0 };
    DWORD read = 0;

    const BOOL ret1 = ReadFile(hFile, (void*)buff, 8, &read, nullptr);
    if (!ret1)
    {
        CloseHandle(hFile);
        return static_cast<int>(GetLastError());
    }

    buff[read] = L'\0';

    if (!wcsncmp(buff, L"junction", 8))
    {
        CloseHandle(hFile);
        return 99;
    }

    CloseHandle(hFile);

    return static_cast<int>(GetLastError());
}

int CallDetouredNtCreateFileOnSymlink(bool withReparsePointFlag)
{
    _NtCreateFile NtCreateFile = GetNtCreateFile();
    _NtClose NtClose = GetNtClose();
    _RtlInitUnicodeString RtlInitUnicodeString = GetRtlInitUnicodeString();

    assert(NtCreateFile != nullptr);
    assert(NtClose  != nullptr);
    assert(RtlInitUnicodeString != nullptr);

    HANDLE hFile = INVALID_HANDLE_VALUE;
    OBJECT_ATTRIBUTES objAttribs = { 0 };

    wstring fullPath;
    if (!TryGetNtFullPath(L"SourceOfSymLink.link", fullPath))
    {
        return static_cast<int>(GetLastError());
    }

    UNICODE_STRING unicodeString{};
    RtlInitUnicodeString(&unicodeString, fullPath.c_str());

    InitializeObjectAttributes(&objAttribs, &unicodeString, OBJ_CASE_INSENSITIVE, NULL, NULL);

    constexpr int allocSize = 2048;
    LARGE_INTEGER largeInteger = { { 0 } };
    largeInteger.QuadPart = allocSize;

    IO_STATUS_BLOCK ioStatusBlock = { 0 };
    const NTSTATUS status = NtCreateFile(
        &hFile,
        FILE_GENERIC_READ,
        &objAttribs,
        &ioStatusBlock,
        &largeInteger,
        FILE_ATTRIBUTE_NORMAL,
        FILE_SHARE_READ,
        FILE_OPEN,
        FILE_NON_DIRECTORY_FILE | static_cast<DWORD>(withReparsePointFlag ? FILE_OPEN_REPARSE_POINT : 0),
        NULL,
        NULL);

    if (hFile != INVALID_HANDLE_VALUE)
    {
        NtClose(hFile);
    }

    return static_cast<int>(RtlNtStatusToDosError(status));
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
        return static_cast<int>(GetLastError());
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
        return static_cast<int>(GetLastError());
    }

    CloseHandle(hFile);

    return static_cast<int>(GetLastError());
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
    const DWORD attributes = GetFileAttributesW(L"directory.lnk");
    if ((attributes & FILE_ATTRIBUTE_DIRECTORY) == 0)
    {
        return -1;
    }

    return static_cast<int>(GetLastError());
}

int CallProbeDirectorySymlinkTarget(bool withReparsePointFlag)
{
    HANDLE hFile = CreateFileW(
        L"directory.lnk",
        0,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        NULL,
        OPEN_EXISTING,
        FILE_FLAG_BACKUP_SEMANTICS | (withReparsePointFlag ? FILE_FLAG_OPEN_REPARSE_POINT : (DWORD)0),
        NULL);

    if (hFile == INVALID_HANDLE_VALUE) 
    {
        return static_cast<int>(GetLastError());
    }

    CloseHandle(hFile);
    return static_cast<int>(GetLastError());
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
        L"AnotherDirectory\\Target_Directory.lnk\\file.lnk",
        GENERIC_READ | GENERIC_WRITE,
        FILE_SHARE_READ,
        0,
        CREATE_NEW,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return static_cast<int>(GetLastError());
    }

    const std::string content = "Some content to write";
    DWORD bytes_written;

    // Write content through the symbolic file link
    WriteFile(hFile, content.c_str(), static_cast<DWORD>(content.size()), &bytes_written, nullptr);
    CloseHandle(hFile);

    return static_cast<int>(GetLastError());
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
        return static_cast<int>(GetLastError());
    }

    CloseHandle(hFile);

    return static_cast<int>(GetLastError());
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
        return static_cast<int>(GetLastError());
    }

    CloseHandle(hFile);

    return static_cast<int>(GetLastError());
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
        return static_cast<int>(GetLastError());
    }

    CloseHandle(hFile);

    // Invalidate directory symlink
    if (!RemoveDirectoryW(L"D.lnk"))
    {
        return static_cast<int>(GetLastError());
    }

    // Recreate the symbolic link chain
    if (!TestCreateSymbolicLinkW(L"D.lnk", L"D2", SYMBOLIC_LINK_FLAG_DIRECTORY))
    {
        return static_cast<int>(GetLastError());
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
        return static_cast<int>(GetLastError());
    }

    CloseHandle(hFile);
    return 0;
}

int CallDeleteSymlinkUnderDirectorySymlinkWithFullSymlinkResolution()
{
    HANDLE hFile = CreateFileW(
        L"D.lnk\\f.lnk",
        GENERIC_WRITE,
        FILE_SHARE_READ | FILE_SHARE_DELETE | FILE_SHARE_WRITE,
        NULL,
        OPEN_EXISTING,
        FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_DELETE_ON_CLOSE,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return static_cast<int>(GetLastError());
    }

    CloseHandle(hFile);
    return 0;
}

int CallOpenNonExistentFileThroughDirectorySymlink()
{
    HANDLE hFile = CreateFileW(
        L"A.lnk\\B\\absent.txt",
        GENERIC_READ,
        FILE_SHARE_READ,
        0,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return static_cast<int>(GetLastError());
    }

    CloseHandle(hFile);

    return static_cast<int>(GetLastError());
}

int CallNtOpenNonExistentFileThroughDirectorySymlink()
{
    HANDLE hFile = INVALID_HANDLE_VALUE;

    wstring fullPath;
    if (!TryGetNtFullPath(L"A.lnk\\B\\absent.txt", fullPath))
    {
        return static_cast<int>(GetLastError());
    }

    const NTSTATUS status = OpenFileWithNtCreateFile(
        &hFile,
        fullPath.c_str(),
        NULL,
        GENERIC_READ,
        FILE_ATTRIBUTE_NORMAL,
        FILE_SHARE_DELETE | FILE_SHARE_READ | FILE_SHARE_WRITE,
        FILE_OPEN,
        FILE_DIRECTORY_FILE);

    if (!NT_SUCCESS(status))
    {
        return static_cast<int>(RtlNtStatusToDosError(status));
    }

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return static_cast<int>(GetLastError());
    }

    CloseHandle(hFile);

    return static_cast<int>(RtlNtStatusToDosError(status));
}

int CallReadFileThroughUntrackedScopeWithFullResolvingEnabledAsync()
{
    HANDLE hFile = CreateFileW(
        L"Untracked\\directory.lnk\\file.txt",
        GENERIC_READ,
        0,
        nullptr,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        nullptr);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return static_cast<int>(GetLastError());
    }

    CloseHandle(hFile);

    return 0;
}

int CallDirectoryEnumerationThroughDirectorySymlink()
{
    WIN32_FIND_DATA ffd{};
    HANDLE hFind = INVALID_HANDLE_VALUE;
    DWORD dwError = 0;

    hFind = FindFirstFile(L"Dir.lnk\\*", &ffd);

    if (INVALID_HANDLE_VALUE == hFind)
    {
        return 21;
    }

    // List all the files
    while (FindNextFile(hFind, &ffd) != 0);

    dwError = GetLastError();
    if (dwError == ERROR_NO_MORE_FILES)
    {
        dwError = ERROR_SUCCESS;
    }

    FindClose(hFind);
    return static_cast<int>(dwError);
}

int CallDeviceIOControlGetReparsePoint()
{
    // Open source symlink
    HANDLE hFile = CreateFileW(
        L"file.lnk",
        0,
        0,
        nullptr,
        OPEN_EXISTING,
        FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS,
        nullptr);
    
    if (hFile == INVALID_HANDLE_VALUE)
    {
        return static_cast<int>(GetLastError());
    }

    // Allocate MAX_PATH to be safe
    constexpr DWORD neededBufSize =
      FIELD_OFFSET(REPARSE_DATA_BUFFER, MountPointReparseBuffer.PathBuffer) +
      2 * MAX_PATH * sizeof(WCHAR);

    BYTE buffer[neededBufSize] = { 0 };

    // Call DeviceIoControl to retrieve the target of the symlink
    DWORD lpBytesReturned = 0;
    const bool result = DeviceIoControl(
        hFile, 
        FSCTL_GET_REPARSE_POINT, 
        nullptr, 
        0,
        buffer, 
        sizeof(buffer), 
        &lpBytesReturned, 
        nullptr);
   
    CloseHandle(hFile);
    
    if (!result)
    {
        return static_cast<int>(GetLastError());
    }

    const REPARSE_DATA_BUFFER* reparseData = (REPARSE_DATA_BUFFER*)buffer;

    // Retrieve the target of the symlink and write it to disk so we can verify on managed side that the target was 
    // actually translated
    std::wstring target;
    target.assign(
        reparseData->SymbolicLinkReparseBuffer.PathBuffer + reparseData->SymbolicLinkReparseBuffer.PrintNameOffset / sizeof(WCHAR),
        static_cast<size_t>(reparseData->SymbolicLinkReparseBuffer.PrintNameLength) / sizeof(WCHAR));

    FILE* output;
    fopen_s(&output, "out.txt", "w");
    assert(output != nullptr);
    fprintf(output, "%ws", target.c_str());
    fclose(output);

    return static_cast<int>(GetLastError());
}

int CallDeviceIOControlSetReparsePoint()
{
    // Open file_example.lnk just to get its reparse point data.
    HANDLE hFile = CreateFileW(
        L"file_example.lnk",
        GENERIC_READ,
        0,
        nullptr,
        OPEN_EXISTING,
        FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS,
        nullptr);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return static_cast<int>(GetLastError());
    }

    // Allocate MAX_PATH to be safe
    constexpr DWORD neededBufSize =
        FIELD_OFFSET(REPARSE_DATA_BUFFER, MountPointReparseBuffer.PathBuffer) +
        2 * MAX_PATH * sizeof(WCHAR);

    BYTE buffer[neededBufSize] = { 0 };

    // Call DeviceIoControl to retrieve the target of the symlink
    DWORD lpBytesReturned = 0;
    bool result = DeviceIoControl(
        hFile,
        FSCTL_GET_REPARSE_POINT,
        nullptr,
        0,
        buffer,
        sizeof(buffer),
        &lpBytesReturned,
        nullptr);

    CloseHandle(hFile);

    if (!result)
    {
        return static_cast<int>(GetLastError());
    }

    // const REPARSE_DATA_BUFFER* reparseData = (REPARSE_DATA_BUFFER*)buffer;
    // std::wstring target;
    // target.assign(
    //     reparseData->SymbolicLinkReparseBuffer.PathBuffer + reparseData->SymbolicLinkReparseBuffer.PrintNameOffset / sizeof(WCHAR),
    //     static_cast<size_t>(reparseData->SymbolicLinkReparseBuffer.PrintNameLength) / sizeof(WCHAR));
    // wprintf(L"Target: %s\n", target.c_str());

    // Use the extracted reparse point data to create a new symlink.
    hFile = CreateFileW(
        L"file.lnk",
        FILE_WRITE_ATTRIBUTES | DELETE | SYNCHRONIZE,
        0,
        nullptr,
        CREATE_NEW,
        FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS,
        nullptr);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return static_cast<int>(GetLastError());
    }
    
    result = DeviceIoControl(
        hFile,
        FSCTL_SET_REPARSE_POINT,
        buffer,
        lpBytesReturned,
        nullptr,
        0,
        nullptr,
        nullptr);

    CloseHandle(hFile);

    if (!result)
    {
        return static_cast<int>(GetLastError());
    }

    // Open the newly created symlink to verify that it was created successfully.
    hFile = CreateFileW(
        L"file.lnk",
        GENERIC_READ,
        0,
        NULL,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return static_cast<int>(GetLastError());
    }

    CloseHandle(hFile);

    return static_cast<int>(GetLastError());
}