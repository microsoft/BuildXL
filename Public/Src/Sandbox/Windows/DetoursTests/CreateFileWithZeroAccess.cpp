// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// CreateFileWithZeroAccess.cpp : Tests opening a handle with no access. Accesses a file called 'input' in the current directory.

#include "stdafx.h"

#ifndef UNICODE
#define UNICODE
#endif

#include <windows.h>
#include <stdio.h>

int CreateFileWithZeroAccess()
{
    HANDLE hFile = CreateFile(
        L"input",
        0,
        0,
        NULL,
        OPEN_EXISTING,
        0,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        wprintf(L"Could not open 'input' (error %lx)\n", GetLastError());
        return 1;
    }

    return 0;
}
