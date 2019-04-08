// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// ReadExclusive.cpp : Tests the exclusive-read scenario. We expect a warning from BuildXL.

#include "stdafx.h"

#include "ReadExclusive.h"

#include <windows.h>
#include <tchar.h>
#include <stdio.h>
#include <strsafe.h>
#include <cstdio>

// ----------------------------------------------------------------------------
// FUNCTION DEFINITIONS
// ----------------------------------------------------------------------------

// ReadExclusive : Tests the exclusive-read scenario. We expect a warning from BuildXL
// when running this test.
//
// Reads ReadExclusive.in from the working directory (location of DetoursTests.exe)
// Writes ReadExclusive.out to the working directory (copies the contents from ReadExclusive.in)
//
// Returns 0 if successful, non-zero if an error occurred. Under BuildXL this should be successful
// because we are reporting exclusive-reads as a warning.
//
// Note: adapted from http://msdn.microsoft.com/en-us/library/ms900134.aspx
int ReadExclusive()
{
    HANDLE hFile, hAppend, hTemp;
    DWORD dwBytesRead, dwBytesWritten, dwPos;
    char buff[4096];

    // Open the existing file.

    hFile = CreateFile(
        TEXT("ReadExclusive.in"),   // Open ReadExclusive.in
        GENERIC_READ,           // Open for reading
        0,                      // Do not share (for reading this should not be allowed)
        NULL,                   // No security
        OPEN_EXISTING,          // Existing file only
        FILE_ATTRIBUTE_NORMAL,  // Normal file
        NULL);                  // No template file

    if (hFile == INVALID_HANDLE_VALUE)
    {
        // Your error-handling code goes here.
        wprintf(TEXT("Could not open 'ReadExclusive.in'\n"));
        return 1;
    }

    // Create a temp file in the temp folder

    hTemp = CreateFile(
        TEXT("temp\\ReadExclusive.tmp"),   // Open ReadExclusive.in
        GENERIC_READ,           // Open for reading
        0,                      // Do not share (opened for read, but with write access permitted; should be allowed)
        NULL,                   // No security
        CREATE_NEW,             // Existing file only
        FILE_ATTRIBUTE_NORMAL,  // Normal file
        NULL);                  // No template file

    if (hTemp == INVALID_HANDLE_VALUE)
    {
        // Your error-handling code goes here.
        wprintf(TEXT("Could not create 'temp\\ReadExclusive.tmp'\n"));
        return 1;
    }


    // Open the existing file, or, if the file does not exist,
    // create a new file.

    hAppend = CreateFile(
        TEXT("ReadExclusive.out"),  // Open ReadExclusive.out.
        GENERIC_WRITE,          // Open for writing
        0,                      // Do not share (for writing this is okay)
        NULL,                   // No security
        OPEN_ALWAYS,            // Open or create
        FILE_ATTRIBUTE_NORMAL,  // Normal file
        NULL);                  // No template file

    if (hAppend == INVALID_HANDLE_VALUE)
    {
        wprintf(L"Could not open 'ReadExclusive.out'\n");
        CloseHandle(hFile);     // Close the first file.
        return 2;
    }

    // Append the first file to the end of the second file.

    dwPos = SetFilePointer(hAppend, 0, NULL, FILE_END);
    do
    {
        if (ReadFile(hFile, buff, 4096, &dwBytesRead, NULL))
        {
            WriteFile(hAppend, buff, dwBytesRead, &dwBytesWritten, NULL);
        }
    } while (dwBytesRead == 4096);

    // Close both files.

    CloseHandle(hFile);
    CloseHandle(hAppend);

    return 0;
}
