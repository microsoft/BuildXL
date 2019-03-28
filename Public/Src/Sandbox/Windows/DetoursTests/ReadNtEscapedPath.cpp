// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// ReadNtEscapedPath.cpp : Tests reading a path starting with \\?\
//  The path read is named 'input', in the current working directory.

#include "stdafx.h"

#ifndef UNICODE
#define UNICODE
#endif

#include <windows.h>
#include <stdio.h>
#include <strsafe.h>
#include <cstdio>

int ReadNtEscapedPath(void)
{
    wchar_t fullPath[MAX_PATH] = {};
    fullPath[0] = L'\\';
    fullPath[1] = L'\\';
    fullPath[2] = L'?';
    fullPath[3] = L'\\';

    DWORD len =  GetFullPathNameW(L"input", _countof(fullPath) - 4, &fullPath[4], nullptr);
    if (len > _countof(fullPath) - 4) {
        wprintf(L"Failed to expand path'\n");
        return 1;
    }

    HANDLE hFile = CreateFile(
        fullPath,
        GENERIC_READ,
        FILE_SHARE_READ,
        NULL,                   
        OPEN_EXISTING,          
        FILE_ATTRIBUTE_NORMAL,  
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        wprintf(L"Could not open %s\n", &fullPath[0]);
        return 1;
    }

    return 0;
}
