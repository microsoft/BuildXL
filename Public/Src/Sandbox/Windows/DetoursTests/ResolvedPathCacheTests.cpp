// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "stdafx.h"

#include <windows.h>
#include <tchar.h>
#include <stdio.h>
#include <strsafe.h>
#include <cstdio>

#if _MSC_VER >= 1200
#pragma warning(disable:4464) // Disable: relative include path contains '..'
#endif


#include "Logging.h"
#include "Utils.h"

#include "ResolvedPathCacheTests.h"

// Used to test the in process ResolvedPathCache 
int ValidateResolvedPathCache() 
{
    std::string content = "Some text";

    // Create a file through a symlink
    HANDLE hFile = CreateFileW(
        L"First_DirectorySymlink\\output.txt",
        GENERIC_WRITE,
        FILE_SHARE_READ | FILE_SHARE_DELETE | FILE_SHARE_WRITE,
        NULL,
        CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }

    DWORD bytes_written;
    if (!WriteFile(hFile, content.c_str(), (DWORD)content.size(), &bytes_written, nullptr))
    {
        return (int)GetLastError();
    }

    CloseHandle(hFile);

    // Read the created file through a symlink
    hFile = CreateFileW(
        L"First_DirectorySymlink\\output.txt",
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

    DWORD bytes_read = 0;
    char buffer[1024];

    if (!ReadFile(hFile, buffer, 1024, &bytes_read, nullptr))
    {
        return (int)GetLastError();
    }

    CloseHandle(hFile);

    // Invalidate the resolved path cache
    if (!RemoveDirectoryW(L"Second_DirectorySymlink"))
    {
        return (int)GetLastError();
    }

    // Recreate the symbolic link chain
    if (!TestCreateSymbolicLinkW(L"Second_DirectorySymlink", L"SourceDirectory", SYMBOLIC_LINK_FLAG_DIRECTORY))
    {
        return (int)GetLastError();
    }

    // Read the created file through a symlink again
    hFile = CreateFileW(
        L"First_DirectorySymlink\\output.txt",
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

    if (!ReadFile(hFile, buffer, 1024, &bytes_read, nullptr))
    {
        return (int)GetLastError();
    }

    CloseHandle(hFile);
    return 0;
}
