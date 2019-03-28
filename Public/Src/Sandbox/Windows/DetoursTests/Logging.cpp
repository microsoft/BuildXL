// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// Logging.cpp : Tests that Logging is working properly for each of the Detoured Windows APIs.
//
// For each of these functions, we expect to see one or more Windows API logging messages
// appear within the BuildXL unit tests.
//
// For each of these API calls, we don't care about the error code, or the actual result of the test.
// It is enough that the call occurs and the program doesn't crash. The call will be logged no matter
// what the result is.
//
// Some of these calls may create files on disk or seem to expect that certain files exist, but
// ultimately we don't care whether those files did exist or will exist, since these tests are not
// concerned with the results of these calls.
//
// For this reason, we always return ERROR_SUCCESS.

#include "stdafx.h"

#include <windows.h>
#include <tchar.h>
#include <stdio.h>
#include <strsafe.h>
#include <cstdio>

#include "Logging.h"
#include "Utils.h"


// ----------------------------------------------------------------------------
// FUNCTION DEFINITIONS
// ----------------------------------------------------------------------------

int CreateProcessWLogging(void)
{
    wchar_t args[200] = L"";

    STARTUPINFOW si;
    ZeroMemory(&si, sizeof(STARTUPINFOW));
    si.cb = sizeof(STARTUPINFOW);

    PROCESS_INFORMATION pi;
    ZeroMemory(&pi, sizeof(PROCESS_INFORMATION));

    CreateProcessW(
        L"DetoursTests.exe",
        args,
        0,
        0,
        0,
        0,
        NULL,
        L"",
        &si,
        &pi
        );

    return ERROR_SUCCESS;
}

int CreateProcessALogging(void)
{
    char args[200] = "";

    STARTUPINFOA si;
    ZeroMemory(&si, sizeof(STARTUPINFOA));
    si.cb = sizeof(STARTUPINFOA);

    PROCESS_INFORMATION pi;
    ZeroMemory(&pi, sizeof(PROCESS_INFORMATION));

    CreateProcessA(
        "DetoursTests.exe",
        args,
        0,
        0,
        0,
        0,
        NULL,
        "",
        &si,
        &pi
        );

    return ERROR_SUCCESS;
}

int CreateFileWLogging(void)
{
    HANDLE hFile = CreateFileW(
        L"CreateFileWLoggingTest.txt",
        GENERIC_READ,
        FILE_SHARE_READ,
        0,
        CREATE_NEW,
        FILE_ATTRIBUTE_NORMAL,
        NULL
        );

    if (hFile == INVALID_HANDLE_VALUE) {
        return 1;
    }

    char message[100] = "Hello, world.";
    DWORD bytesWritten;
    WriteFile(hFile, message, 20, &bytesWritten, NULL);

    CloseHandle(hFile);

    return ERROR_SUCCESS;
}

int CreateFileALogging(void)
{
    HANDLE hFile = CreateFileA(
        "CreateFileALoggingTest.txt",
        GENERIC_READ,
        FILE_SHARE_READ,
        0,
        CREATE_NEW,
        FILE_ATTRIBUTE_NORMAL,
        NULL
        );

    if (hFile == INVALID_HANDLE_VALUE) {
        return 1;
    }

    char message[100] = "Hello, world.";
    DWORD bytesWritten;
    WriteFile(hFile, message, 20, &bytesWritten, NULL);

    CloseHandle(hFile);

    return ERROR_SUCCESS;
}

int GetVolumePathNameWLogging(void)
{
    const unsigned int LENGTH = 200;
    wchar_t pathName[LENGTH];

    GetVolumePathNameW(
        L"GetVolumePathNameWLoggingTest.txt",
        pathName,
        LENGTH
        );

    return ERROR_SUCCESS;
}

int GetFileAttributesWLogging(void)
{
    GetFileAttributesW(L"GetFileAttributesWLoggingTest.txt");

    return ERROR_SUCCESS;
}

int GetFileAttributesALogging(void)
{
    GetFileAttributesA("GetFileAttributesALoggingTest.txt");

    return ERROR_SUCCESS;
}

int GetFileAttributesExWLogging(void)
{
    GetFileAttributesExW(
        L"GetFileAttributesALoggingTest.txt",
        (GET_FILEEX_INFO_LEVELS)GetFileExInfoStandard,
        NULL
        );

    return ERROR_SUCCESS;
}

int GetFileAttributesExALogging(void)
{
    GetFileAttributesExA(
        "GetFileAttributesALoggingTest.txt",
        (GET_FILEEX_INFO_LEVELS)GetFileExInfoStandard,
        NULL
        );

    return ERROR_SUCCESS;
}

int CopyFileWLogging(void)
{
    return CopyFileW(
        L"CopyFileWLoggingTest1.txt",
        L"CopyFileWLoggingTest2.txt",
        false
        ) ? 0 : 1;
}

int CopyFileALogging(void)
{
    return CopyFileA(
        "CopyFileALoggingTest1.txt",
        "CopyFileALoggingTest2.txt",
        false
        ) ? 0 : 1;
}

int CopyFileExWLogging(void)
{
    CopyFileExW(
        L"CopyFileExWLoggingTest1.txt",
        L"CopyFileExWLoggingTest2.txt",
        NULL,
        NULL,
        false,
        0
        );

    return ERROR_SUCCESS;
}

int CopyFileExALogging(void)
{
    CopyFileExA(
        "CopyFileExALoggingTest1.txt",
        "CopyFileExALoggingTest2.txt",
        NULL,
        NULL,
        false,
        0
        );

    return ERROR_SUCCESS;
}

int MoveFileWLogging(void)
{
    return MoveFileW(
        L"MoveFileWLoggingTest1.txt",
        L"MoveFileWLoggingTest2.txt"
        ) ? 0 : 1;
}

int MoveFileALogging(void)
{
    return MoveFileA(
        "MoveFileALoggingTest1.txt",
        "MoveFileALoggingTest2.txt"
        ) ? 0 : 1;
}

int MoveFileExWLogging(void)
{
    MoveFileExW(
        L"MoveFileExWLoggingTest1.txt",
        L"MoveFileExWLoggingTest2.txt",
        0
        );

    return ERROR_SUCCESS;
}

int MoveFileExALogging(void)
{
    MoveFileExA(
        "MoveFileExALoggingTest1.txt",
        "MoveFileExALoggingTest2.txt",
        0
        );

    return ERROR_SUCCESS;
}

int MoveFileWithProgressWLogging(void)
{
    MoveFileWithProgressW(
        L"MoveFileWithProgressWLoggingTest1.txt",
        L"MoveFileWithProgressWLoggingTest2.txt",
        NULL,
        0,
        0
        );

    return ERROR_SUCCESS;
}

int MoveFileWithProgressALogging(void)
{
    MoveFileWithProgressA(
        "MoveFileWithProgressALoggingTest1.txt",
        "MoveFileWithProgressALoggingTest2.txt",
        NULL,
        0,
        0
        );

    return ERROR_SUCCESS;
}

int ReplaceFileWLogging(void)
{
    ReplaceFileW(
        L"ReplaceFileWLoggingTestIn.txt",
        L"ReplaceFileWLoggingTestOut.txt",
        L"ReplaceFileWLoggingTestBackup.txt",
        0,
        NULL,
        NULL
        );

    return ERROR_SUCCESS;
}

int ReplaceFileALogging(void)
{
    ReplaceFileA(
        "ReplaceFileALoggingTestIn.txt",
        "ReplaceFileALoggingTestOut.txt",
        "ReplaceFileALoggingTestBackup.txt",
        0,
        NULL,
        NULL
        );

    return ERROR_SUCCESS;
}

int DeleteFileWLogging(void)
{
    DeleteFileW(L"DeleteFileWLoggingTest.txt");

    return ERROR_SUCCESS;
}

int DeleteFileALogging(void)
{
    DeleteFileA("DeleteFileALoggingTest.txt");

    return ERROR_SUCCESS;
}

int CreateHardLinkWLogging(void)
{
    CreateHardLinkW(
        L"CreateHardLinkWLoggingTest1.txt",
        L"CreateHardLinkWLoggingTest2.txt",
        0
        );

    return ERROR_SUCCESS;
}

int CreateHardLinkALogging(void)
{
    CreateHardLinkA(
        "CreateHardLinkALoggingTest1.txt",
        "CreateHardLinkALoggingTest2.txt",
        0
        );

    return ERROR_SUCCESS;
}

int CreateSymbolicLinkWLogging(void)
{
    TestCreateSymbolicLinkW(
        L"CreateSymbolicLinkWLoggingTest1.txt",
        L"CreateSymbolicLinkWLoggingTest2.txt",
        0
        );

    return ERROR_SUCCESS;
}

int CreateSymbolicLinkALogging(void)
{
    TestCreateSymbolicLinkA(
        "CreateSymbolicLinkALoggingTest1.txt",
        "CreateSymbolicLinkALoggingTest2.txt",
        0
        );

    return ERROR_SUCCESS;
}

int FindFirstFileWLogging(void)
{
    FindFirstFileW(
        L"FindFirstFileWLoggingTest.txt",
        NULL
        );

    return ERROR_SUCCESS;
}

int FindFirstFileALogging(void)
{
    FindFirstFileA(
        "FindFirstFileALoggingTest.txt",
        NULL
        );

    return ERROR_SUCCESS;
}

int FindFirstFileExWLogging(void)
{
    FindFirstFileExW(
        L"FindFirstFileExWLoggingTest.txt",
        (FINDEX_INFO_LEVELS)FindExInfoStandard,
        NULL,
        (FINDEX_SEARCH_OPS)FindExSearchNameMatch,
        NULL,
        0
        );

    return ERROR_SUCCESS;
}

int FindFirstFileExALogging(void)
{
    FindFirstFileExA(
        "FindFirstFileExALoggingTest.txt",
        (FINDEX_INFO_LEVELS)FindExInfoStandard,
        NULL,
        (FINDEX_SEARCH_OPS)FindExSearchNameMatch,
        NULL,
        0
        );

    return ERROR_SUCCESS;
}

int GetFileInformationByHandleExLogging(void)
{
    HANDLE hFile = NULL;
    GetFileInformationByHandleEx(
        hFile,
        (FILE_INFO_BY_HANDLE_CLASS)FileBasicInfo,
        NULL,
        100
        );

    return ERROR_SUCCESS;
}

int SetFileInformationByHandleLogging(void)
{
    HANDLE hFile = NULL;
    SetFileInformationByHandle(
        hFile,
        (FILE_INFO_BY_HANDLE_CLASS)FileBasicInfo,
        NULL,
        100
        );

    return ERROR_SUCCESS;
}

int OpenFileMappingWLogging(void)
{
    OpenFileMappingW(
        GENERIC_READ,
        false,
        L"OpenFileMappingWLoggingTest.txt"
        );

    return ERROR_SUCCESS;
}

int OpenFileMappingALogging(void)
{
    OpenFileMappingA(
        GENERIC_READ,
        false,
        "OpenFileMappingALoggingTest.txt"
        );

    return ERROR_SUCCESS;
}

int GetTempFileNameWLogging(void)
{
    wchar_t tempDir[MAX_PATH];
    GetTempPathW(MAX_PATH, tempDir);

    wchar_t tempFile[MAX_PATH];

    GetTempFileNameW(
        tempDir,
        L"Tst",
        0,
        tempFile
        );

    return ERROR_SUCCESS;
}

int GetTempFileNameALogging(void)
{
    char tempDir[MAX_PATH];
    GetTempPathA(MAX_PATH, tempDir);

    char tempFile[MAX_PATH];

    GetTempFileNameA(
        tempDir,
        "Tst",
        0,
        tempFile
        );

    return ERROR_SUCCESS;
}

int CreateDirectoryWLogging(void)
{
    BOOL success = CreateDirectoryW(
        L"CreateDirectoryWLoggingTest",
        0
        );
    return success ? 0 : ((GetLastError() == ERROR_ALREADY_EXISTS) ? 0 : 1);
}

int CreateDirectoryALogging(void)
{
    BOOL success = CreateDirectoryA(
        "CreateDirectoryALoggingTest",
        0
        );
    return success ? 0 : ((GetLastError() == ERROR_ALREADY_EXISTS) ? 0 : 1);
}

int CreateDirectoryExWLogging(void)
{
    CreateDirectoryExW(
        L"CreateDirectoryExWLoggingTestTemplateDirectory",
        L"CreateDirectoryExWLoggingTest",
        0
        );

    return ERROR_SUCCESS;
}

int CreateDirectoryExALogging(void)
{
    CreateDirectoryExA(
        "CreateDirectoryExALoggingTestTemplateDirectory",
        "CreateDirectoryExALoggingTest",
        0
        );

    return ERROR_SUCCESS;
}

int RemoveDirectoryWLogging(void)
{
    RemoveDirectoryW(L"RemoveDirectoryWLoggingTest");

    return ERROR_SUCCESS;
}

int RemoveDirectoryALogging(void)
{
    RemoveDirectoryA("RemoveDirectoryALoggingTest");

    return ERROR_SUCCESS;
}

int DecryptFileWLogging(void)
{
    DecryptFileW(
        L"DecryptFileWLoggingTest.txt",
        0
        );

    return ERROR_SUCCESS;
}

int DecryptFileALogging(void)
{
    DecryptFileA(
        "DecryptFileALoggingTest.txt",
        0
        );

    return ERROR_SUCCESS;
}

int EncryptFileWLogging(void)
{
    EncryptFileW(L"EncryptFileWLoggingTest.txt");

    return ERROR_SUCCESS;
}

int EncryptFileALogging(void)
{
    EncryptFileA("EncryptFileALoggingTest.txt");

    return ERROR_SUCCESS;
}

int OpenEncryptedFileRawWLogging(void)
{
    OpenEncryptedFileRawW(
        L"OpenEncryptedFileRawWTest.txt",
        0,
        NULL
        );

    return ERROR_SUCCESS;
}

int OpenEncryptedFileRawALogging(void)
{
    OpenEncryptedFileRawA(
        "OpenEncryptedFileRawATest.txt",
        0,
        NULL
        );

    return ERROR_SUCCESS;
}

int OpenFileByIdLogging(void)
{
    HANDLE hFile = NULL;
    OpenFileById(
        hFile,
        NULL,
        0,
        0,
        0,
        0
        );

    return ERROR_SUCCESS;
}
