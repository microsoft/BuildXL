// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// --------------------------------------------------------------------

// stdafx.h : include file for standard system include files,
// or project specific include files that are used frequently, but
// are changed infrequently
//

#pragma once

// Disable warnings about unreferenced inline functions. We'd do this below but this disable has to be in effect
// at optimization time.
#pragma warning( disable : 4514 4710 )

// We don't care about the addition of needed struct padding.
#pragma warning( disable : 4820 )

// Domino should run on Win7+.
#include <WinSDKVer.h>
#define _WIN32_WINNT _WIN32_WINNT_WIN7
#include <SDKDDKVer.h>

// In order to compile with /Wall (mega pedantic warnings), we need to turn off a few that the Windows SDK violates.
// We could do this in stdafx.cpp so long as a precompiled header is being generated, since the compiler state from
// that file (including warning state!) would be dumped to the .pch - instead, we stick to the sane compilation
// model and twiddle warning state at include time. This means that disabling .pch generation doesn't result in weird warnings.
#pragma warning( push )
#pragma warning( disable : 4350 4668 )
#include <windows.h>
#include <winternl.h>
#include <stdarg.h>
#include <stdio.h>
#include <assert.h>
#include <string>
#include <vector>
#include <memory>
#include <iostream>
#pragma warning( pop )

// These are taken from ntifs.h in the DDK. Ideally we could include it directly via DDK package.
extern "C" {
    typedef struct _FILE_DIRECTORY_INFORMATION {
        ULONG NextEntryOffset;
        ULONG FileIndex;
        LARGE_INTEGER CreationTime;
        LARGE_INTEGER LastAccessTime;
        LARGE_INTEGER LastWriteTime;
        LARGE_INTEGER ChangeTime;
        LARGE_INTEGER EndOfFile;
        LARGE_INTEGER AllocationSize;
        ULONG FileAttributes;
        ULONG FileNameLength;
        WCHAR FileName[1];
    } FILE_DIRECTORY_INFORMATION, *PFILE_DIRECTORY_INFORMATION;

    NTSTATUS NTAPI NtQueryDirectoryFile(
        _In_ HANDLE FileHandle,
        _In_opt_ HANDLE Event,
        _In_opt_ PIO_APC_ROUTINE ApcRoutine,
        _In_opt_ PVOID ApcContext,
        _Out_ PIO_STATUS_BLOCK IoStatusBlock,
        _Out_writes_bytes_(Length) PVOID FileInformation,
        _In_ ULONG Length,
        _In_ FILE_INFORMATION_CLASS FileInformationClass,
        _In_ BOOLEAN ReturnSingleEntry,
        _In_opt_ PUNICODE_STRING FileName,
        _In_ BOOLEAN RestartScan
        );

    // From ntstatus.h in the DDK

    //
    // MessageId: STATUS_NO_MORE_FILES
    //
    // MessageText:
    //
    // {No More Files}
    // No more files were found which match the file specification.
    //
    #define STATUS_NO_MORE_FILES             ((NTSTATUS)0x80000006L)
}