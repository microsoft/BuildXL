// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma once

#include "DataTypes.h"
#include "DetouredFunctionTypes.h"
#include "DetouredProcessInjector.h"

#include <vector>

using std::vector;

// ----------------------------------------------------------------------------
// DEFINES
// ----------------------------------------------------------------------------

#define SUPER_VERBOSE 0
#define MEASURE_DETOURED_NT_CLOSE_IMPACT 0

// ----------------------------------------------------------------------------
// FORWARD DECLARATIONS
// ----------------------------------------------------------------------------
class TranslatePathTuple;
class ShimProcessMatch;

// ----------------------------------------------------------------------------
// GLOBALS
// ----------------------------------------------------------------------------

extern SpecialProcessKind  g_ProcessKind;

extern HANDLE g_hPrivateHeap;

// Not referenced, but useful during debugging.
extern PVOID g_manifestPtr;
extern PDWORD g_manifestSizePtr;
extern DWORD g_currentProcessId;
extern PCWSTR g_currentProcessCommandLine;

extern FileAccessManifestFlag g_fileAccessManifestFlags;
extern FileAccessManifestExtraFlag g_fileAccessManifestExtraFlags;
extern uint64_t g_FileAccessManifestPipId;

extern PCManifestRecord g_manifestTreeRoot;

extern PManifestTranslatePathsStrings g_manifestTranslatePathsStrings;
extern vector<TranslatePathTuple*>* g_pManifestTranslatePathTuples;

extern PManifestInternalDetoursErrorNotificationFileString g_manifestInternalDetoursErrorNotificationFileString;
extern LPCTSTR g_internalDetoursErrorNotificationFile;

extern HANDLE g_messageCountSemaphore;

extern HANDLE g_reportFileHandle;

extern unsigned long g_injectionTimeoutInMinutes;

extern bool g_BreakOnAccessDenied;

extern LPCSTR g_lpDllNameX86;
extern LPCSTR g_lpDllNameX64;

/// The filter callback function that must be implemented as an extern "C" __declspec(dllexport) BOOL WINAPI ShouldRunShim(...)
/// function exported from the substitute process execution filter DLL. One 32-bit and one 64-bit DLL must be provided to
/// match the DetoursServices.dll flavor used for wrapping a process.
///
/// Returns TRUE or nonzero if the prospective process should have the shim process injected. Returns FALSE or zero otherwise.
///
/// Note for implementors: Process creation is halted for this process until this callback returns.
/// WINAPI is used for register call efficiency.
///
/// command: The executable command. Can be a fully qualified path, relative path, or unqualified path
/// that needs a PATH search.
///
/// arguments: The arguments to the command. May be an empty string.
///
/// environmentBlock: The environment block for the process. The format is a sequence of "var=value"
/// null-terminated strings, with an empty string (i.e. double null character) terminator. Note that
/// values can have equals signs in them; only the first equals sign is the variable name separator.
/// See more formatting info in the lpEnvironment parameter description at
/// https://docs.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessa
///
/// workingDirectory: The working directory for the command.
typedef BOOL (__stdcall * SubstituteProcessExecutionFilterFunc)(const wchar_t* command, const wchar_t* arguments, LPVOID environmentBlock, const wchar_t* workingDirectory);

extern wchar_t *g_SubstituteProcessExecutionShimPath;
extern wchar_t *g_SubstituteProcessExecutionFilterDLLPath;
extern HMODULE g_SubstituteProcessExecutionFilterDLLHandle;
extern SubstituteProcessExecutionFilterFunc g_SubstituteProcessExecutionFilterFunc;
extern bool g_ProcessExecutionShimAllProcesses;
extern vector<ShimProcessMatch*>* g_pShimProcessMatches;

extern DetouredProcessInjector* g_pDetouredProcessInjector;

//
// Real Windows API function pointers
//

extern CreateProcessW_t Real_CreateProcessW;
extern CreateProcessA_t Real_CreateProcessA;
extern CreateFileW_t Real_CreateFileW;

extern RtlFreeHeap_t Real_RtlFreeHeap;
extern RtlAllocateHeap_t Real_RtlAllocateHeap;
extern RtlReAllocateHeap_t Real_RtlReAllocateHeap;
extern VirtualAlloc_t Real_VirtualAlloc;

extern CreateFileA_t Real_CreateFileA;
extern GetVolumePathNameW_t Real_GetVolumePathNameW;
extern GetFileAttributesA_t Real_GetFileAttributesA;
extern GetFileAttributesW_t Real_GetFileAttributesW;
extern GetFileAttributesExW_t Real_GetFileAttributesExW;
extern GetFileAttributesExA_t Real_GetFileAttributesExA;
extern CloseHandle_t Real_CloseHandle;

extern GetFileInformationByHandle_t Real_GetFileInformationByHandle;
extern GetFileInformationByHandleEx_t Real_GetFileInformationByHandleEx;
extern SetFileInformationByHandle_t Real_SetFileInformationByHandle;

extern CopyFileW_t Real_CopyFileW;
extern CopyFileA_t Real_CopyFileA;
extern CopyFileExW_t Real_CopyFileExW;
extern CopyFileExA_t Real_CopyFileExA;
extern MoveFileW_t Real_MoveFileW;
extern MoveFileA_t Real_MoveFileA;
extern MoveFileExW_t Real_MoveFileExW;
extern MoveFileExA_t Real_MoveFileExA;
extern MoveFileWithProgressW_t Real_MoveFileWithProgressW;
extern MoveFileWithProgressA_t Real_MoveFileWithProgressA;
extern ReplaceFileW_t Real_ReplaceFileW;
extern ReplaceFileA_t Real_ReplaceFileA;
extern DeleteFileA_t Real_DeleteFileA;
extern DeleteFileW_t Real_DeleteFileW;

extern CreateHardLinkW_t Real_CreateHardLinkW;
extern CreateHardLinkA_t Real_CreateHardLinkA;
extern CreateSymbolicLinkW_t Real_CreateSymbolicLinkW;
extern CreateSymbolicLinkA_t Real_CreateSymbolicLinkA;
extern FindFirstFileW_t Real_FindFirstFileW;
extern FindFirstFileA_t Real_FindFirstFileA;
extern FindFirstFileExW_t Real_FindFirstFileExW;
extern FindFirstFileExA_t Real_FindFirstFileExA;
extern FindNextFileA_t Real_FindNextFileA;
extern FindNextFileW_t Real_FindNextFileW;
extern FindClose_t Real_FindClose;
extern OpenFileMappingW_t Real_OpenFileMappingW;
extern OpenFileMappingA_t Real_OpenFileMappingA;
extern GetTempFileNameW_t Real_GetTempFileNameW;
extern GetTempFileNameA_t Real_GetTempFileNameA;
extern CreateDirectoryW_t Real_CreateDirectoryW;
extern CreateDirectoryA_t Real_CreateDirectoryA;
extern CreateDirectoryExW_t Real_CreateDirectoryExW;
extern CreateDirectoryExA_t Real_CreateDirectoryExA;
extern RemoveDirectoryW_t Real_RemoveDirectoryW;
extern RemoveDirectoryA_t Real_RemoveDirectoryA;
extern DecryptFileW_t Real_DecryptFileW;
extern DecryptFileA_t Real_DecryptFileA;
extern EncryptFileW_t Real_EncryptFileW;
extern EncryptFileA_t Real_EncryptFileA;
extern OpenEncryptedFileRawW_t Real_OpenEncryptedFileRawW;
extern OpenEncryptedFileRawA_t Real_OpenEncryptedFileRawA;
extern OpenFileById_t Real_OpenFileById;
extern GetFinalPathNameByHandleW_t Real_GetFinalPathNameByHandleW;
extern GetFinalPathNameByHandleA_t Real_GetFinalPathNameByHandleA;

extern NtClose_t Real_NtClose;
extern NtCreateFile_t Real_NtCreateFile;
extern NtOpenFile_t Real_NtOpenFile;
extern ZwCreateFile_t Real_ZwCreateFile;
extern ZwOpenFile_t Real_ZwOpenFile;
extern NtQueryDirectoryFile_t Real_NtQueryDirectoryFile;
extern ZwQueryDirectoryFile_t Real_ZwQueryDirectoryFile;
extern ZwSetInformationFile_t Real_ZwSetInformationFile;

#if MEASURE_DETOURED_NT_CLOSE_IMPACT
extern volatile LONG g_msTimeToPopulatePoolList;
extern volatile ULONGLONG g_pipExecutionStart;
extern volatile LONG g_ntCloseHandeCount;
extern volatile LONG g_maxClosedListCount;
extern volatile LONG g_msTimeInAddClosedList;
extern volatile LONG g_msTimeInRemoveClosedList;
#endif // MEASURE_DETOURED_NT_CLOSE_IMPACT
