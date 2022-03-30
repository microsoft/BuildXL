// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// CorrelationCalls.cpp : Tests how Detours correlates file operations of some Detoured functions.

#include "stdafx.h"

#ifndef UNICODE
#define UNICODE
#endif

#include <windows.h>
#include <stdio.h>
#include "CorrelationCalls.h"
#include "Utils.h"

int CorrelateCopyFile()
{
    CopyFileW(L"SourceFile.txt", L"DestinationFile.txt", FALSE);
    return (int)GetLastError();
}

int CorrelateCreateHardLink()
{
    CreateHardLink(L"DestinationFile.txt", L"SourceFile.txt", NULL);
    return (int)GetLastError();
}

int CorrelateMoveFile()
{
    MoveFileW(L"Source\\SourceFile.txt", L"DestinationFile.txt");
    return (int)GetLastError();
}

int CorrelateMoveDirectory()
{
    MoveFileExW(L"Directory\\SourceDirectory", L"Directory\\DestinationDirectory", MOVEFILE_COPY_ALLOWED);
    return (int)GetLastError();
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

    return (int)GetLastError();
}