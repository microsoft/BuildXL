// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// Timestamps.cpp : Tests timestamp-faking in Detours for input files (see WellKnownTimestamps.NewInputTimestamp)
// and visibility of real (but well-known) timestamps for re-written outputs (see WellKnownTimestamps.OldOutputTimestamp).
// 
// Expects these files:
//   input (an input file)
//   rewrittenOutput (a rewritten output file)
//   subdir\rewrittenOutput1
//   subdir\rewrittenOutput2
//   subdir\input1
//   subdir\input2
//   sharedOpaque\sourceSealInSharedOpaque\inputInSourceSealInSharedOpaque
//   sharedOpaque\subdir\nested\staticInputInSharedOpaque
//   sharedOpaque\anothersubdir\nested\dynamicInputInSharedOpaque1
//   sharedOpaque\anothersubdir\dynamicInputInSharedOpaque2
//   sharedOpaque\dynamicInputInSharedOpaque3
//   sharedOpaque\rewrittenOutputInSharedOpaque
//
// There are two of each file type in subdir to guarantee that both types can appear in FindNextFile when enumerating the directory.


#include "stdafx.h"

#include "Timestamps.h"
#include "VerificationResult.h"

#include <stdio.h>
#include <strsafe.h>
#include <cstdio>

#pragma warning( push )
#pragma warning( disable : 4350 )
#include <map>
#include <string>
#pragma warning( pop )

#pragma warning( disable : 4711) // ... selected for inline expansion

static bool ExpectExistent(wchar_t const* filename) {
    DWORD attributes = GetFileAttributesW(filename);
    if (attributes == INVALID_FILE_ATTRIBUTES) {
        wprintf(L"Expected the input file to exist: %s\n", filename);
        return false;
    }

    return true;
}

FILETIME ConvertSystemTimeToFileTime(SYSTEMTIME const& time) {
    FILETIME fileTime;
    if (!SystemTimeToFileTime(&time, &fileTime)) {
        throw std::exception("Failed to convert a SYSTEMTIME to FILETIME");
    }

    return fileTime;
}

FILETIME GetExpectedInputTime() {
    SYSTEMTIME time{};
    time.wYear = 2002;
    time.wMonth = 2;
    time.wDay = 2;
    time.wHour = 2;
    time.wMinute = 2;
    time.wSecond = 2;

    return ConvertSystemTimeToFileTime(time);
}

FILETIME GetExpectedOutputTime() {
    SYSTEMTIME time{};
    time.wYear = 2001;
    time.wMonth = 1;
    time.wDay = 1;
    time.wHour = 1;
    time.wMinute = 1;
    time.wSecond = 1;

    return ConvertSystemTimeToFileTime(time);
}

VerificationResult VerifyTimestamp(FILETIME expectedTimestamp, FILETIME actualTimestamp, wchar_t const* description, wchar_t const* filename, bool allowGreaterThan) {
    
    if (allowGreaterThan)
    {
        if (CompareFileTime(&expectedTimestamp, &actualTimestamp) != -1) {
            wprintf(L"Wrong timestamp [%s on %s]: expected greater than or equal to %08lx%08lx != actual %08lx%08lx\n",
                description,
                filename,
                expectedTimestamp.dwHighDateTime, expectedTimestamp.dwLowDateTime,
                actualTimestamp.dwHighDateTime, actualTimestamp.dwLowDateTime);
            return false;
        }
    }
    else
    {
        if ((expectedTimestamp.dwHighDateTime != actualTimestamp.dwHighDateTime) ||
            (expectedTimestamp.dwLowDateTime != actualTimestamp.dwLowDateTime)) {
            wprintf(L"Wrong timestamp [%s on %s]: expected %08lx%08lx != actual %08lx%08lx\n",
                description,
                filename,
                expectedTimestamp.dwHighDateTime, expectedTimestamp.dwLowDateTime,
                actualTimestamp.dwHighDateTime, actualTimestamp.dwLowDateTime);
            return false;
        }
    }

    return true;
}

VerificationResult VerifyTimestamp(FILETIME expectedTimestamp, LARGE_INTEGER actualTimestamp, wchar_t const* description, wchar_t const* filename, bool allowGreaterThan) {
    FILETIME actualTimestampAsFiletime{};
    actualTimestampAsFiletime.dwLowDateTime = actualTimestamp.LowPart;
    actualTimestampAsFiletime.dwHighDateTime = (DWORD)actualTimestamp.HighPart;
    return VerifyTimestamp(expectedTimestamp, actualTimestampAsFiletime, description, filename, allowGreaterThan);
}

VerificationResult VerifyExpectedTimestampViaGetFileAttributesEx(wchar_t const * filename, FILETIME expectedTimestamp, bool allowGreaterThan) {
    WIN32_FILE_ATTRIBUTE_DATA data{};
    if (!GetFileAttributesExW(filename, GET_FILEEX_INFO_LEVELS::GetFileExInfoStandard, &data)) {
        wprintf(L"GetFileAttributesEx failed for %s (error %08lx)\n", filename, GetLastError());
        return false;
    }

    VerificationResult result;
    result.Combine(VerifyTimestamp(expectedTimestamp, data.ftCreationTime, L"GetFileAttributesEx() -> ftCreationTime", filename, allowGreaterThan));
    result.Combine(VerifyTimestamp(expectedTimestamp, data.ftLastWriteTime, L"GetFileAttributesEx() -> ftLastWriteTime", filename, allowGreaterThan));
    result.Combine(VerifyTimestamp(expectedTimestamp, data.ftLastAccessTime, L"GetFileAttributesEx() -> ftLastAccessTime", filename, allowGreaterThan));
    return result;
}

VerificationResult VerifyExpectedTimestampViaGetFileInformationByHandle(wchar_t const * filename, FILETIME expectedTimestamp, bool allowGreaterThan) {
    HANDLE handle = CreateFileW(filename, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_DELETE | FILE_SHARE_WRITE, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, INVALID_HANDLE_VALUE);
    if (handle == INVALID_HANDLE_VALUE) {
        wprintf(L"CreateFileW failed for %s (error %08lx)\n", filename, GetLastError());
        return false;
    }
    
    BY_HANDLE_FILE_INFORMATION byHandleInfo{};
    if (!GetFileInformationByHandle(handle, &byHandleInfo)) {
        wprintf(L"GetFileInformationByHandle failed for %s (error %08lx)\n", filename, GetLastError());
        return false;
    }

    VerificationResult result;
    result.Combine(VerifyTimestamp(expectedTimestamp, byHandleInfo.ftCreationTime, L"GetFileInformationByHandle() -> ftCreationTime", filename, allowGreaterThan));
    result.Combine(VerifyTimestamp(expectedTimestamp, byHandleInfo.ftLastWriteTime, L"GetFileInformationByHandle() -> ftLastWriteTime", filename, allowGreaterThan));
    result.Combine(VerifyTimestamp(expectedTimestamp, byHandleInfo.ftLastAccessTime, L"GetFileInformationByHandle() -> ftLastAccessTime", filename, allowGreaterThan));
    return result;
}

VerificationResult VerifyExpectedTimestampViaGetFileInformationByHandleEx(wchar_t const * filename, FILETIME expectedTimestamp, bool allowGreaterThan) {
    HANDLE handle = CreateFileW(filename, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_DELETE | FILE_SHARE_WRITE, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, INVALID_HANDLE_VALUE);
    if (handle == INVALID_HANDLE_VALUE) {
        wprintf(L"CreateFileW failed for %s (error %08lx)\n", filename, GetLastError());
        return false;
    }

    FILE_BASIC_INFO basicInfo{};
    if (!GetFileInformationByHandleEx(handle, FileBasicInfo, &basicInfo, sizeof(basicInfo))) {
        wprintf(L"GetFileInformationByHandleEx failed for %s (error %08lx)\n", filename, GetLastError());
        return false;
    }

    VerificationResult result;
    result.Combine(VerifyTimestamp(expectedTimestamp, basicInfo.CreationTime, L"GetFileInformationByHandleEx() -> CreationTime", filename, allowGreaterThan));
    result.Combine(VerifyTimestamp(expectedTimestamp, basicInfo.LastWriteTime, L"GetFileInformationByHandleEx() -> LastWriteTime", filename, allowGreaterThan));
    result.Combine(VerifyTimestamp(expectedTimestamp, basicInfo.LastAccessTime, L"GetFileInformationByHandleEx() -> LastAccessTime", filename, allowGreaterThan));
    result.Combine(VerifyTimestamp(expectedTimestamp, basicInfo.ChangeTime, L"GetFileInformationByHandleEx() -> ChangeTime", filename, allowGreaterThan));
    return result;
}

// FindFirstFileEx without a wildcard.
VerificationResult VerifyExpectedTimestampViaFindFirstFileSingle(wchar_t const * filename, FILETIME expectedTimestamp, bool allowGreaterThan) {
    WIN32_FIND_DATAW findData{};
    HANDLE findHandle = FindFirstFileExW(filename, FindExInfoBasic, &findData, FindExSearchNameMatch, NULL, 0);
    if (findHandle == INVALID_HANDLE_VALUE) {
        wprintf(L"FindFirstFileExW failed for %s (error %08lx)\n", filename, GetLastError());
        return false;
    }

    VerificationResult result;
    result.Combine(VerifyTimestamp(expectedTimestamp, findData.ftCreationTime, L"FindFirstFileEx() -> ftCreationTime", filename, allowGreaterThan));
    result.Combine(VerifyTimestamp(expectedTimestamp, findData.ftLastWriteTime, L"FindFirstFileEx() -> ftLastWriteTime", filename, allowGreaterThan));
    result.Combine(VerifyTimestamp(expectedTimestamp, findData.ftLastAccessTime, L"FindFirstFileEx() -> ftLastAccessTime", filename, allowGreaterThan));

    if (FindNextFileW(findHandle, &findData)) {
        wprintf(L"FindNextFileW should not have succeeded; expecting a single-file match for %s\n", filename);
        result = false;
    }

    FindClose(findHandle);

    return result;
}

// FindFirstFileEx with a wildcard and possibly multiple expectations.
VerificationResult VerifyExpectedTimestampViaFindFirstFileEnumeration(wchar_t const * filename, std::map<std::wstring, FILETIME> expectations, bool allowGreaterThan) {
    VerificationResult result;
    
    WIN32_FIND_DATAW findData{};
    HANDLE findHandle = FindFirstFileExW(filename, FindExInfoBasic, &findData, FindExSearchNameMatch, NULL, 0);
    if (findHandle != INVALID_HANDLE_VALUE) {
        do {
            // Skip the magic . and .. entries
            if (findData.cFileName[0] == L'.' && (findData.cFileName[1] == L'.' || findData.cFileName[1] == L'\0')) {
                continue;
            }

            _wcslwr_s(findData.cFileName);
            std::wstring foundNameLower(&findData.cFileName[0]);
            
            auto findIter = expectations.find(foundNameLower);
            if (findIter == expectations.cend()) {
                wprintf(L"Enumeration of %s found %s for which there was no timestamp expectation set.\n", filename, &findData.cFileName[0]);
                result.Combine(false);
                continue;
            }

            FILETIME expectedTimestamp = findIter->second;
            expectations.erase(findIter);

            result.Combine(VerifyTimestamp(expectedTimestamp, findData.ftCreationTime, L"FindFirstFile enumeration -> ftCreationTime", filename, allowGreaterThan));
            result.Combine(VerifyTimestamp(expectedTimestamp, findData.ftLastWriteTime, L"FindFirstFile enumeration -> ftLastWriteTime", filename, allowGreaterThan));
            result.Combine(VerifyTimestamp(expectedTimestamp, findData.ftLastAccessTime, L"FindFirstFile enumeration -> ftLastAccessTime", filename, allowGreaterThan));
        } while (FindNextFileW(findHandle, &findData));

        FindClose(findHandle);
    }

    if (!expectations.empty()) {
        unsigned long long remaining = expectations.size();
        wprintf(L"Enumeration of %s left %llu expectations remaining (files not found).\n", filename, remaining);
        result.Combine(false);
    }

    return result;
}

void VerifyExpectedTimestampForAllKnownFunctions(VerificationResult& verificationResult, wchar_t const * filename, FILETIME expectedTimestamp, bool allowGreaterThan) {
    verificationResult.Combine(VerifyExpectedTimestampViaGetFileAttributesEx(filename, expectedTimestamp, allowGreaterThan));
    verificationResult.Combine(VerifyExpectedTimestampViaGetFileInformationByHandle(filename, expectedTimestamp, allowGreaterThan));
    verificationResult.Combine(VerifyExpectedTimestampViaGetFileInformationByHandleEx(filename, expectedTimestamp, allowGreaterThan));
    verificationResult.Combine(VerifyExpectedTimestampViaFindFirstFileSingle(filename, expectedTimestamp, allowGreaterThan));
}

int Timestamps(bool normalize)
{
    wchar_t const * const InputFile = L"input";
    wchar_t const * const RewrittenOutputFile = L"rewrittenOutput";

    // Note that these have to be lowercase, as a silly detail of VerifyExpectedTimestampViaFindFirstFileEnumeration
    wchar_t const * const SubdirInputFile1 = L"input1";
    wchar_t const * const SubdirInputFile2 = L"input2";
    wchar_t const * const SubdirRewrittenOutputFile1 = L"rewrittenoutput1";
    wchar_t const * const SubdirRewrittenOutputFile2 = L"rewrittenoutput2";
    wchar_t const * const InputInSourceSealInSharedOpaque = L"sharedOpaque\\sourceSealInSharedOpaque\\inputInSourceSealInSharedOpaque";
    wchar_t const * const StaticInputInSharedOpaque = L"sharedOpaque\\subdir\\nested\\staticInputInSharedOpaque";
    wchar_t const * const DynamicInputInSharedOpaque1 = L"sharedOpaque\\anothersubdir\\nested\\dynamicInputInSharedOpaque1";
    wchar_t const * const DynamicInputInSharedOpaque2 = L"sharedOpaque\\anothersubdir\\dynamicInputInSharedOpaque2";
    wchar_t const * const DynamicInputInSharedOpaque3 = L"sharedOpaque\\dynamicInputInSharedOpaque3";
    wchar_t const * const RewrittenOutputInSharedOpaque = L"sharedOpaque\\rewrittenOutputInSharedOpaque";
    wchar_t const * const DynamicOutputInSharedOpaque = L"sharedOpaque\\yetanothersubdir\\dynamicOutputInSharedOpaque"; // does not exist, this process creates it
    wchar_t const * const AnotherDynamicOutputInSharedOpaque = L"sharedOpaque\\subdir\\dynamicOutputInSharedOpaque"; // does not exist, this process creates it

    FILETIME expectedInputTime = GetExpectedInputTime();
    FILETIME expectedOutputTime = GetExpectedOutputTime();

    if (!ExpectExistent(InputFile) || 
        !ExpectExistent(RewrittenOutputFile) ||
        !ExpectExistent(L"subdir\\input1") ||
        !ExpectExistent(L"subdir\\input2") ||
        !ExpectExistent(L"subdir\\rewrittenOutput1") ||
        !ExpectExistent(L"subdir\\rewrittenOutput2") ||
        !ExpectExistent(InputInSourceSealInSharedOpaque) ||
        !ExpectExistent(StaticInputInSharedOpaque) ||
        !ExpectExistent(DynamicInputInSharedOpaque1) ||
        !ExpectExistent(DynamicInputInSharedOpaque2) ||
        !ExpectExistent(DynamicInputInSharedOpaque3) ||
        !ExpectExistent(RewrittenOutputInSharedOpaque)) {
        return 1;
    }
    
    // Create two dynamic outputs under the shared opaque, we want to verify timestamp faking does not happen for outputs
    // The first dynamic output is created in a directory that does not contain any inputs
    BOOL success = CreateDirectory(L"sharedOpaque\\yetanothersubdir", nullptr);
    if (!success)
    {
        return (int)GetLastError();
    }
    HANDLE hFile = CreateFile(
        DynamicOutputInSharedOpaque,
        GENERIC_WRITE,
        FILE_SHARE_WRITE,
        0,
        CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL,
        NULL
    );
        
    if (hFile == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }

    char message[100] = "Hello, world.";
    DWORD bytesWritten;
    WriteFile(hFile, message, 20, &bytesWritten, NULL);
    CloseHandle(hFile);

    // The second dynamic output is created in a directory that contains inputs
    HANDLE anotherHFile = CreateFile(
        AnotherDynamicOutputInSharedOpaque,
        GENERIC_WRITE,
        FILE_SHARE_WRITE,
        0,
        CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL,
        NULL
    );

    if (anotherHFile == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }
    WriteFile(anotherHFile, message, 20, &bytesWritten, NULL);
    CloseHandle(anotherHFile);

    bool allowGreaterThan = !normalize;

    VerificationResult result;
    VerifyExpectedTimestampForAllKnownFunctions(result, InputFile, expectedInputTime, allowGreaterThan);
    VerifyExpectedTimestampForAllKnownFunctions(result, RewrittenOutputFile, expectedOutputTime, false);
    VerifyExpectedTimestampForAllKnownFunctions(result, InputInSourceSealInSharedOpaque, expectedInputTime, allowGreaterThan);
    VerifyExpectedTimestampForAllKnownFunctions(result, StaticInputInSharedOpaque, expectedInputTime, allowGreaterThan);
    VerifyExpectedTimestampForAllKnownFunctions(result, DynamicInputInSharedOpaque1, expectedInputTime, allowGreaterThan);
    VerifyExpectedTimestampForAllKnownFunctions(result, DynamicInputInSharedOpaque2, expectedInputTime, allowGreaterThan);
    VerifyExpectedTimestampForAllKnownFunctions(result, DynamicInputInSharedOpaque3, expectedInputTime, allowGreaterThan);
    VerifyExpectedTimestampForAllKnownFunctions(result, RewrittenOutputInSharedOpaque, expectedOutputTime, false);
    VerifyExpectedTimestampForAllKnownFunctions(result, DynamicOutputInSharedOpaque, expectedOutputTime, true);
    // This is to verify that even though timestamp faking happens for the parent directory (checked below), the output itself shows its true timestamp
    VerifyExpectedTimestampForAllKnownFunctions(result, AnotherDynamicOutputInSharedOpaque, expectedOutputTime, true);

    // Verify that we also fake the timestamp of directories that involve dynamic and static inputs under a shared opaque
    result.Combine(VerifyExpectedTimestampViaGetFileAttributesEx(L"sharedOpaque\\subdir", expectedInputTime, allowGreaterThan));
    result.Combine(VerifyExpectedTimestampViaGetFileAttributesEx(L"sharedOpaque\\subdir\\nested", expectedInputTime, allowGreaterThan));
    result.Combine(VerifyExpectedTimestampViaGetFileAttributesEx(L"sharedOpaque\\anothersubdir", expectedInputTime, allowGreaterThan));
    result.Combine(VerifyExpectedTimestampViaGetFileAttributesEx(L"sharedOpaque\\anothersubdir\\nested", expectedInputTime, allowGreaterThan));

    // Verify that we don't fake the timestamp of directories that do not involve inputs
    result.Combine(VerifyExpectedTimestampViaGetFileAttributesEx(L"sharedOpaque\\yetanothersubdir", expectedOutputTime, true));

    result.Combine(VerifyExpectedTimestampViaFindFirstFileEnumeration(L"subdir\\input*", { 
        { SubdirInputFile1, expectedInputTime },
        { SubdirInputFile2, expectedInputTime },
    }, allowGreaterThan));

    result.Combine(VerifyExpectedTimestampViaFindFirstFileEnumeration(L"subdir\\rewrittenOutput*", {
        { SubdirRewrittenOutputFile1, expectedOutputTime },
        { SubdirRewrittenOutputFile2, expectedOutputTime },
    }, false));

    result.Combine(VerifyExpectedTimestampViaFindFirstFileEnumeration(L"subdir\\input*", {
        { SubdirInputFile1, expectedInputTime },
        { SubdirInputFile2, expectedInputTime },
    }, allowGreaterThan));

    result.Combine(VerifyExpectedTimestampViaFindFirstFileEnumeration(L"subdir\\rewrittenoutput*", {
        { SubdirRewrittenOutputFile1, expectedOutputTime },
        { SubdirRewrittenOutputFile2, expectedOutputTime },
    }, false));

    return result.Succeeded ? 0 : 2;
}

int TimestampsNormalize(void)
{
    return Timestamps(true);
}

int TimestampsNoNormalize(void)
{
    return Timestamps(false);
}
