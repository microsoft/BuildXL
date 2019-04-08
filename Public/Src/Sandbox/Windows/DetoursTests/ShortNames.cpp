// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// ShortNames.cpp : Tests hiding of short names in detours. These tests should pass trivially if the test volume has short name generation disabled.
// 
// Expects one file:
//   directoryWithAVeryLongName\fileWithAVeryLongName

#include "stdafx.h"
#include "ShortNames.h"
#include "VerificationResult.h"

#include <stdio.h>
#include <strsafe.h>
#include <cstdio>

#pragma warning( disable : 4711) // ... selected for inline expansion

static bool ExpectExistent(wchar_t const* filename) {
    DWORD attributes = GetFileAttributesW(filename);
    if (attributes == INVALID_FILE_ATTRIBUTES) {
        wprintf(L"Expected the input file to exist: %s\n", filename);
        return false;
    }

    return true;
}

VerificationResult VerifyPathDoesNotContainShortPathMarker(wchar_t const* description, wchar_t const* longPath, wchar_t const* pathToCheck) {
    bool hasShortPathMarker = wcschr(pathToCheck, '~') != nullptr;
    
    if (hasShortPathMarker) {
        wprintf(L"Path or name contains sort path marker [%s on %s]: %s\n",
            description,
            longPath,
            pathToCheck);
    }

    return !hasShortPathMarker;
}

VerificationResult VerifyShortNamesAbsentViaFindFirstFile(wchar_t const * filename) {
    WIN32_FIND_DATAW findData{};
    HANDLE findHandle = FindFirstFileExW(filename, FindExInfoBasic, &findData, FindExSearchNameMatch, NULL, 0);
    if (findHandle == INVALID_HANDLE_VALUE) {
        wprintf(L"FindFirstFileExW failed for %s (error %08lx)\n", filename, GetLastError());
        return false;
    }

    VerificationResult result = VerifyPathDoesNotContainShortPathMarker(
        L"FindFirstFileExW",
        filename,
        &findData.cAlternateFileName[0]);

    if (FindNextFileW(findHandle, &findData)) {
        wprintf(L"FindNextFileW should not have succeeded; expecting a single-file match for %s\n", filename);
        result = false;
    }

    FindClose(findHandle);

    return result; 
}

// Expansion of a path with GetShortPathName
VerificationResult VerifyShortNamesAbsentViaGetShortPathName(wchar_t const * filename) {
    wchar_t buffer[MAX_PATH] {};
    DWORD count = GetShortPathNameW(filename, &buffer[0], MAX_PATH);
    if (count >= MAX_PATH || count == 0) {
        wprintf(L"GetShortPathNameW failed: %lx\r\n", GetLastError());
        return false;
    }

    return VerifyPathDoesNotContainShortPathMarker(
        L"GetShortPathNameW",
        filename,
        &buffer[0]);
}

int ShortNames()
{
    wchar_t const * const TestDirectory = L"directoryWithAVeryLongName";
    wchar_t const * const TestFile = L"directoryWithAVeryLongName\\fileWithAVeryLongName";

    if (!ExpectExistent(TestDirectory) ||
        !ExpectExistent(TestFile)) {
        return 1;
    }

    VerificationResult result;
    result.Combine(VerifyShortNamesAbsentViaFindFirstFile(TestDirectory));
    result.Combine(VerifyShortNamesAbsentViaFindFirstFile(TestFile));

    result.Combine(VerifyShortNamesAbsentViaGetShortPathName(TestDirectory));
    result.Combine(VerifyShortNamesAbsentViaGetShortPathName(TestFile));

    // TODO: Could scrub FILE_ID_BOTH_DIR_INFO too (https://msdn.microsoft.com/en-us/library/windows/desktop/aa364226(v=vs.85).aspx)

    return result.Succeeded ? 0 : 2;
}
