// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// CorrelationCalls.cpp : Tests how Detours correlates file operations of some Detoured functions.

#include "stdafx.h"

#ifndef UNICODE
#define UNICODE
#endif

#include <windows.h>
#include <ktmw32.h>
#include <stdio.h>
#include "CorrelationCalls.h"
#include "Utils.h"

// warning C26472: Don't use a static_cast for arithmetic conversions. Use brace initialization, gsl::narrow_cast or gsl::narrow (type.1).
#pragma warning( disable : 26472 )

int CorrelateCopyFile()
{
    CopyFileW(L"SourceFile.txt", L"DestinationFile.txt", FALSE);
    return static_cast<int>(GetLastError());
}

int CorrelateCopyFileTransacted()
{
    HANDLE hTransaction = CreateTransaction(NULL, 0, 0, 0, 0, 0, NULL);
    if (hTransaction == INVALID_HANDLE_VALUE)
    {
        return static_cast<int>(GetLastError());
    }

    DWORD lastError = ERROR_SUCCESS;
    const BOOL result = CopyFileTransactedW(L"SourceFile.txt", L"DestinationFile.txt", NULL, NULL, FALSE, COPY_FILE_FAIL_IF_EXISTS, hTransaction);
    if (result)
    {
        CommitTransaction(hTransaction); // Commit the transaction
    }
    else 
    {
        lastError = GetLastError();
        RollbackTransaction(hTransaction); // Rollback the transaction
    }

    CloseHandle(hTransaction);
    return static_cast<int>(lastError);
}

int CorrelateCreateHardLink()
{
    CreateHardLink(L"DestinationFile.txt", L"SourceFile.txt", NULL);
    return static_cast<int>(GetLastError());
}

int CorrelateMoveFile()
{
    MoveFileW(L"Source\\SourceFile.txt", L"DestinationFile.txt");
    return static_cast<int>(GetLastError());
}

int CorrelateMoveFileTransacted()
{
    HANDLE hTransaction = CreateTransaction(NULL, 0, 0, 0, 0, 0, NULL);
    if (hTransaction == INVALID_HANDLE_VALUE)
    {
        return static_cast<int>(GetLastError());
    }

    DWORD lastError = ERROR_SUCCESS;

    const BOOL result = MoveFileTransactedW(L"Source\\SourceFile.txt", L"DestinationFile.txt", NULL, NULL, MOVEFILE_COPY_ALLOWED, hTransaction);
    if (result)
    {
        CommitTransaction(hTransaction); // Commit the transaction
    }
    else
    {
        lastError = GetLastError();
        RollbackTransaction(hTransaction); // Rollback the transaction
    }

    CloseHandle(hTransaction);
    return static_cast<int>(lastError);
}

int CorrelateMoveDirectory()
{
    MoveFileExW(L"Directory\\SourceDirectory", L"Directory\\DestinationDirectory", MOVEFILE_COPY_ALLOWED);
    return static_cast<int>(GetLastError());
}

int CorrelateMoveDirectoryTransacted()
{
    HANDLE hTransaction = CreateTransaction(NULL, 0, 0, 0, 0, 0, NULL);
    if (hTransaction == INVALID_HANDLE_VALUE)
    {
        return static_cast<int>(GetLastError());
    }
    DWORD lastError = ERROR_SUCCESS;
    const BOOL result = MoveFileTransactedW(L"Directory\\SourceDirectory", L"Directory\\DestinationDirectory", NULL, NULL, MOVEFILE_COPY_ALLOWED, hTransaction);
    if (result)
    {
        CommitTransaction(hTransaction); // Commit the transaction
    }
    else
    {
        lastError = GetLastError();
        RollbackTransaction(hTransaction); // Rollback the transaction
    }

    CloseHandle(hTransaction);
    return static_cast<int>(lastError);
}

int CorrelateRenameDirectory()
{
    HANDLE hSourceDirectory = CreateFileW(
        L"Directory\\SourceDirectory",
        GENERIC_READ | GENERIC_WRITE | DELETE,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        NULL,
        OPEN_EXISTING,
        FILE_FLAG_BACKUP_SEMANTICS,
        NULL);

    SetRenameFileByHandle(hSourceDirectory, L"Directory\\DestinationDirectory", true);

    CloseHandle(hSourceDirectory);

    return static_cast<int>(GetLastError());
}