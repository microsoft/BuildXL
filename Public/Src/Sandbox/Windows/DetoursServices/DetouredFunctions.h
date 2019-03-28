// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/*
Declaractions for detoured function handlers
*/

#pragma once

#include "DetoursHelpers.h"
#include "FileAccessHelpers.h"

// Data structures - from ntifs.h
typedef struct _REPARSE_DATA_BUFFER {
    ULONG  ReparseTag;
    USHORT ReparseDataLength;
    USHORT Reserved;
    union {
        struct {
            USHORT SubstituteNameOffset;
            USHORT SubstituteNameLength;
            USHORT PrintNameOffset;
            USHORT PrintNameLength;
            ULONG  Flags;
            WCHAR  PathBuffer[1];
        } SymbolicLinkReparseBuffer;
        struct {
            USHORT SubstituteNameOffset;
            USHORT SubstituteNameLength;
            USHORT PrintNameOffset;
            USHORT PrintNameLength;
            WCHAR  PathBuffer[1];
        } MountPointReparseBuffer;
        struct {
            UCHAR DataBuffer[1];
        } GenericReparseBuffer;
    };
} REPARSE_DATA_BUFFER, *PREPARSE_DATA_BUFFER;


// ----------------------------------------------------------------------------
// FUNCTION DECLARATIONS
// ----------------------------------------------------------------------------

// See CreateProcess on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/ms682425(v=vs.85).aspx
BOOL WINAPI Detoured_CreateProcessW(
    __in_opt      LPCWSTR lpApplicationName,
    __inout_opt   LPWSTR lpCommandLine,
    __in_opt      LPSECURITY_ATTRIBUTES lpProcessAttributes,
    __in_opt      LPSECURITY_ATTRIBUTES lpThreadAttributes,
    __in          BOOL bInheritHandles,
    __in          DWORD dwCreationFlags,
    __in_opt      LPVOID lpEnvironment,
    __in_opt      LPCWSTR lpCurrentDirectory,
    __in          LPSTARTUPINFOW lpStartupInfo,
    __out         LPPROCESS_INFORMATION lpProcessInformation
    );

// See CreateProcess on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/ms682425(v=vs.85).aspx
BOOL WINAPI Detoured_CreateProcessA(
    __in_opt      LPCSTR lpApplicationName,
    __inout_opt   LPSTR lpCommandLine,
    __in_opt      LPSECURITY_ATTRIBUTES lpProcessAttributes,
    __in_opt      LPSECURITY_ATTRIBUTES lpThreadAttributes,
    __in          BOOL bInheritHandles,
    __in          DWORD dwCreationFlags,
    __in_opt      LPVOID lpEnvironment,
    __in_opt      LPCSTR lpCurrentDirectory,
    __in          LPSTARTUPINFOA lpStartupInfo,
    __out         LPPROCESS_INFORMATION lpProcessInformation
    );

// See CreateFile on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa363858(v=vs.85).aspx
HANDLE WINAPI Detoured_CreateFileW(
    __in     LPCWSTR lpFileName,
    __in     DWORD dwDesiredAccess,
    __in     DWORD dwShareMode,
    __in_opt LPSECURITY_ATTRIBUTES lpSecurityAttributes,
    __in     DWORD dwCreationDisposition,
    __in     DWORD dwFlagsAndAttributes,
    __in_opt HANDLE hTemplateFile
    );

// See CreateFile on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa363858(v=vs.85).aspx
HANDLE WINAPI Detoured_CreateFileA(
    __in     LPCSTR lpFileName,
    __in     DWORD dwDesiredAccess,
    __in     DWORD dwShareMode,
    __in_opt LPSECURITY_ATTRIBUTES lpSecurityAttributes,
    __in     DWORD dwCreationDisposition,
    __in     DWORD dwFlagsAndAttributes,
    __in_opt HANDLE hTemplateFile
    );

// See CloseHandle on MSDN: https://msdn.microsoft.com/en-us/library/windows/desktop/ms724211%28v=vs.85%29.aspx
BOOL WINAPI Detoured_CloseHandle(
    __in    HANDLE handle
    );

// See GetVolumePathName on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa364996(v=vs.85).aspx
BOOL WINAPI Detoured_GetVolumePathNameW(
    __in                          LPCWSTR lpszFileName,
    __out_ecount(cchBufferLength) LPWSTR lpszVolumePathName,
    __in                          DWORD cchBufferLength
    );

// See GetFileAttributes on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa915578(v=vs.85).aspx
DWORD WINAPI Detoured_GetFileAttributesW(
    __in  LPCWSTR lpFileName
    );

// See GetFileAttributes on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa915578(v=vs.85).aspx
DWORD WINAPI Detoured_GetFileAttributesA(
    __in  LPCSTR lpFileName
    );

// See GetFileAttributesEx on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa914422(v=vs.85).aspx
BOOL WINAPI Detoured_GetFileAttributesExW(
    __in  LPCWSTR lpFileName,
    __in  GET_FILEEX_INFO_LEVELS fInfoLevelId,
    __out LPVOID lpFileInformation
    );

// See GetFileAttributesEx on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa914422(v=vs.85).aspx
BOOL WINAPI Detoured_GetFileAttributesExA(
    __in  LPCSTR lpFileName,
    __in  GET_FILEEX_INFO_LEVELS fInfoLevelId,
    __out LPVOID lpFileInformation
    );

// See CopyFile on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa363851(v=vs.85).aspx
BOOL WINAPI Detoured_CopyFileW(
    __in  LPCWSTR lpExistingFileName,
    __in  LPCWSTR lpNewFileName,
    __in  BOOL bFailIfExists
    );

// See CopyFile on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa363851(v=vs.85).aspx
BOOL WINAPI Detoured_CopyFileA(
    __in  LPCSTR lpExistingFileName,
    __in  LPCSTR lpNewFileName,
    __in  BOOL bFailIfExists
    );

// See CopyFileEx on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa363852(v=vs.85).aspx
BOOL WINAPI Detoured_CopyFileExW(
    __in      LPCWSTR lpExistingFileName,
    __in      LPCWSTR lpNewFileName,
    __in_opt  LPPROGRESS_ROUTINE lpProgressRoutine,
    __in_opt  LPVOID lpData,
    __in_opt  LPBOOL pbCancel,
    __in      DWORD dwCopyFlags
    );

// See CopyFileEx on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa363852(v=vs.85).aspx
BOOL WINAPI Detoured_CopyFileExA(
    __in      LPCSTR lpExistingFileName,
    __in      LPCSTR lpNewFileName,
    __in_opt  LPPROGRESS_ROUTINE lpProgressRoutine,
    __in_opt  LPVOID lpData,
    __in_opt  LPBOOL pbCancel,
    __in      DWORD dwCopyFlags
    );

// See MoveFile on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa365239(v=vs.85).aspx
BOOL WINAPI Detoured_MoveFileW(
    __in  LPCWSTR lpExistingFileName,
    __in  LPCWSTR lpNewFileName
    );

// See MoveFile on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa365239(v=vs.85).aspx
BOOL WINAPI Detoured_MoveFileA(
    __in  LPCSTR lpExistingFileName,
    __in  LPCSTR lpNewFileName
    );

// See MoveFileEx on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa365240(v=vs.85).aspx
BOOL WINAPI Detoured_MoveFileExW(
    __in      LPCWSTR lpExistingFileName,
    __in_opt  LPCWSTR lpNewFileName,
    __in      DWORD dwFlags
    );

// See MoveFileEx on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa365240(v=vs.85).aspx
BOOL WINAPI Detoured_MoveFileExA(
    __in      LPCSTR lpExistingFileName,
    __in_opt  LPCSTR lpNewFileName,
    __in      DWORD dwFlags
    );

// See MoveFileWithProgress on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa365242(v=vs.85).aspx
BOOL WINAPI Detoured_MoveFileWithProgressW(
    __in      LPCWSTR lpExistingFileName,
    __in_opt  LPCWSTR lpNewFileName,
    __in_opt  LPPROGRESS_ROUTINE lpProgressRoutine,
    __in_opt  LPVOID lpData,
    __in      DWORD dwFlags
    );

// See MoveFileWithProgress on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa365242(v=vs.85).aspx
BOOL WINAPI Detoured_MoveFileWithProgressA(
    __in      LPCSTR lpExistingFileName,
    __in_opt  LPCSTR lpNewFileName,
    __in_opt  LPPROGRESS_ROUTINE lpProgressRoutine,
    __in_opt  LPVOID lpData,
    __in      DWORD dwFlags
    );

// See ReplaceFile on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa365512(v=vs.85).aspx
BOOL WINAPI Detoured_ReplaceFileW(
    __in        LPCWSTR lpReplacedFileName,
    __in        LPCWSTR lpReplacementFileName,
    __in_opt    LPCWSTR lpBackupFileName,
    __in        DWORD dwReplaceFlags,
    __reserved  LPVOID lpExclude,
    __reserved  LPVOID lpReserved
    );

// See ReplaceFile on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa365512(v=vs.85).aspx
BOOL WINAPI Detoured_ReplaceFileA(
    __in        LPCSTR lpReplacedFileName,
    __in        LPCSTR lpReplacementFileName,
    __in_opt    LPCSTR lpBackupFileName,
    __in        DWORD dwReplaceFlags,
    __reserved  LPVOID lpExclude,
    __reserved  LPVOID lpReserved
    );

// See DeleteFile on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa363915(v=vs.85).aspx
BOOL WINAPI Detoured_DeleteFileW(
    __in LPCWSTR lpFileName
    );

// See DeleteFile on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa363915(v=vs.85).aspx
BOOL WINAPI Detoured_DeleteFileA(
    __in LPCSTR lpFileName
    );

// See CreateHardLink on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa363860(v=vs.85).aspx
BOOL WINAPI Detoured_CreateHardLinkW(
    __in        LPCWSTR lpFileName,
    __in        LPCWSTR lpExistingFileName,
    __reserved  LPSECURITY_ATTRIBUTES lpSecurityAttributes
    );

// See CreateHardLink on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa363860(v=vs.85).aspx
BOOL WINAPI Detoured_CreateHardLinkA(
    __in        LPCSTR lpFileName,
    __in        LPCSTR lpExistingFileName,
    __reserved  LPSECURITY_ATTRIBUTES lpSecurityAttributes
    );

// See CreateSymbolicLink on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa363866(v=vs.85).aspx
BOOLEAN WINAPI Detoured_CreateSymbolicLinkW(
    __in  LPCWSTR lpSymlinkFileName,
    __in  LPCWSTR lpTargetFileName,
    __in  DWORD dwFlags
    );

// See CreateSymbolicLink on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa363866(v=vs.85).aspx
BOOLEAN WINAPI Detoured_CreateSymbolicLinkA(
    __in  LPCSTR lpSymlinkFileName,
    __in  LPCSTR lpTargetFileName,
    __in  DWORD dwFlags
    );

// See FindFirstFile on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa364418(v=vs.85).aspx
HANDLE WINAPI Detoured_FindFirstFileW(
    __in   LPCWSTR lpFileName,
    __out  LPWIN32_FIND_DATAW lpFindFileData
    );

// See FindFirstFile on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa364418(v=vs.85).aspx
HANDLE WINAPI Detoured_FindFirstFileA(
    __in   LPCSTR lpFileName,
    __out  LPWIN32_FIND_DATAA lpFindFileData
    );

// See FindFirstFileEx on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa364419(v=vs.85).aspx
HANDLE WINAPI Detoured_FindFirstFileExW(
    __in        LPCWSTR lpFileName,
    __in        FINDEX_INFO_LEVELS fInfoLevelId,
    __out       LPVOID lpFindFileData,
    __in        FINDEX_SEARCH_OPS fSearchOp,
    __reserved  LPVOID lpSearchFilter,
    __in        DWORD dwAdditionalFlags
    );

// See FindFirstFileEx on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa364419(v=vs.85).aspx
HANDLE WINAPI Detoured_FindFirstFileExA(
    __in        LPCSTR lpFileName,
    __in        FINDEX_INFO_LEVELS fInfoLevelId,
    __out       LPVOID lpFindFileData,
    __in        FINDEX_SEARCH_OPS fSearchOp,
    __reserved  LPVOID lpSearchFilter,
    __in        DWORD dwAdditionalFlags
    );

// See FindNextFile on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa364428(v=vs.85).aspx
BOOL WINAPI Detoured_FindNextFileW(
    __in   HANDLE hFindFile,
    __out  LPWIN32_FIND_DATAW lpFindFileData
    );

// See FindNextFile on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa364428(v=vs.85).aspx
BOOL WINAPI Detoured_FindNextFileA(
    __in   HANDLE hFindFile,
    __out  LPWIN32_FIND_DATAA lpFindFileData
    );

// See FindClose on MSDN: https://msdn.microsoft.com/en-us/library/windows/desktop/aa364413%28v=vs.85%29.aspx
BOOL WINAPI Detoured_FindClose(
    __in   HANDLE hFindFile
    );

// See GetFileInformationByHandleEx on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa364953(v=vs.85).aspx
BOOL WINAPI Detoured_GetFileInformationByHandleEx(
    __in   HANDLE hFile,
    __in   FILE_INFO_BY_HANDLE_CLASS FileInformationClass,
    __out  LPVOID lpFileInformation,
    __in   DWORD dwBufferSize
    );

// See GetFileInformationByHandle on MSDN: https://msdn.microsoft.com/en-us/library/windows/desktop/aa364952(v=vs.85).aspx
BOOL WINAPI Detoured_GetFileInformationByHandle(
    __in   HANDLE hFile,
    __out  LPBY_HANDLE_FILE_INFORMATION lpFileInformation
    );

// See SetFileInformationByHandle on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa365539(v=vs.85).aspx
BOOL WINAPI Detoured_SetFileInformationByHandle(
    __in  HANDLE hFile,
    __in  FILE_INFO_BY_HANDLE_CLASS FileInformationClass,
    __in  LPVOID lpFileInformation,
    __in  DWORD dwBufferSize
    );

// TODO:add CreateFileMapping*

// See OpenFileMapping on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa366791(v=vs.85).aspx
HANDLE WINAPI Detoured_OpenFileMappingW(
    __in  DWORD dwDesiredAccess,
    __in  BOOL bInheritHandle,
    __in  LPCWSTR lpName
    );

// See OpenFileMapping on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa366791(v=vs.85).aspx
HANDLE WINAPI Detoured_OpenFileMappingA(
    __in  DWORD dwDesiredAccess,
    __in  BOOL bInheritHandle,
    __in  LPCSTR lpName
    );

// See GetTempFileName on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa364991(v=vs.85).aspx
UINT WINAPI Detoured_GetTempFileNameW(
    __in   LPCWSTR lpPathName,
    __in   LPCWSTR lpPrefixString,
    __in   UINT uUnique,
    __out  LPTSTR lpTempFileName
    );

// See GetTempFileName on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa364991(v=vs.85).aspx
UINT WINAPI Detoured_GetTempFileNameA(
    __in   LPCSTR lpPathName,
    __in   LPCSTR lpPrefixString,
    __in   UINT uUnique,
    __out  LPSTR lpTempFileName
    );

// See CreateDirectory on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa363855(v=vs.85).aspx
BOOL WINAPI Detoured_CreateDirectoryW(
    __in      LPCWSTR lpPathName,
    __in_opt  LPSECURITY_ATTRIBUTES lpSecurityAttributes
    );

// See CreateDirectory on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa363855(v=vs.85).aspx
BOOL WINAPI Detoured_CreateDirectoryA(
    __in      LPCSTR lpPathName,
    __in_opt  LPSECURITY_ATTRIBUTES lpSecurityAttributes
    );


// See CreateDirectoryEx on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa363856(v=vs.85).aspx
BOOL WINAPI Detoured_CreateDirectoryExW(
    __in      LPCWSTR lpTemplateDirectory,
    __in      LPCWSTR lpNewDirectory,
    __in_opt  LPSECURITY_ATTRIBUTES lpSecurityAttributes
    );

// See CreateDirectoryEx on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa363856(v=vs.85).aspx
BOOL WINAPI Detoured_CreateDirectoryExA(
    __in      LPCSTR lpTemplateDirectory,
    __in      LPCSTR lpNewDirectory,
    __in_opt  LPSECURITY_ATTRIBUTES lpSecurityAttributes
    );

// See RemoveDirectory on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa365488(v=vs.85).aspx
BOOL WINAPI Detoured_RemoveDirectoryW(
    __in  LPCWSTR lpPathName
    );

// See RemoveDirectory on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa365488(v=vs.85).aspx
BOOL WINAPI Detoured_RemoveDirectoryA(
    __in  LPCSTR lpPathName
    );

// See DecryptFile on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa363903(v=vs.85).aspx
BOOL WINAPI Detoured_DecryptFileW(
    __in        LPCWSTR lpFileName,
    __reserved  DWORD dwReserved
    );

// See DecryptFile on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa363903(v=vs.85).aspx
BOOL WINAPI Detoured_DecryptFileA(
    __in        LPCSTR lpFileName,
    __reserved  DWORD dwReserved
    );

// See EncryptFile on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa364021(v=vs.85).aspx
BOOL WINAPI Detoured_EncryptFileW(
    __in  LPCWSTR lpFileName
    );

// See EncryptFile on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa364021(v=vs.85).aspx
BOOL WINAPI Detoured_EncryptFileA(
    __in  LPCSTR lpFileName
    );

// See OpenEncryptedFileRaw on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa365429(v=vs.85).aspx
DWORD WINAPI Detoured_OpenEncryptedFileRawW(
    __in   LPCWSTR lpFileName,
    __in   ULONG ulFlags,
    __out  PVOID *pvContext
    );

// See OpenEncryptedFileRaw on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa365429(v=vs.85).aspx
DWORD WINAPI Detoured_OpenEncryptedFileRawA(
    __in   LPCSTR lpFileName,
    __in   ULONG ulFlags,
    __out  PVOID *pvContext
    );

// See OpenFileById on MSDN: http://msdn.microsoft.com/en-us/library/windows/desktop/aa365432(v=vs.85).aspx
HANDLE WINAPI Detoured_OpenFileById(
    __in      HANDLE hFile,
    __in      LPFILE_ID_DESCRIPTOR lpFileID,
    __in      DWORD dwDesiredAccess,
    __in      DWORD dwShareMode,
    __in_opt  LPSECURITY_ATTRIBUTES lpSecurityAttributes,
    __in      DWORD dwFlags
    );

// See GetFinalPathNameByHandle on MSDN: https://msdn.microsoft.com/en-us/library/windows/desktop/aa364962(v=vs.85).aspx
DWORD WINAPI Detoured_GetFinalPathNameByHandleW(
    __in  HANDLE hFile,
    __out LPTSTR lpszFilePath,
    __in  DWORD cchFilePath,
    __in  DWORD dwFlags
    );

// See GetFinalPathNameByHandle on MSDN: https://msdn.microsoft.com/en-us/library/windows/desktop/aa364962(v=vs.85).aspx
DWORD WINAPI Detoured_GetFinalPathNameByHandleA(
    __in  HANDLE hFile,
    __out LPSTR lpszFilePath,
    __in  DWORD cchFilePath,
    __in  DWORD dwFlags
    );

// See NtQueryDirectoryFile on MSDN: https://msdn.microsoft.com/en-us/library/windows/hardware/ff556633(v=vs.85).aspx
NTSTATUS NTAPI Detoured_NtQueryDirectoryFile(
    __in HANDLE FileHandle,
    __in_opt HANDLE Event,
    __in_opt PIO_APC_ROUTINE ApcRoutine,
    __in_opt PVOID ApcContext,
    __out PIO_STATUS_BLOCK IoStatusBlock,
    __out_bcount(Length) PVOID FileInformation,
    __in ULONG Length,
    __in FILE_INFORMATION_CLASS FileInformationClass,
    __in BOOLEAN ReturnSingleEntry,
    __in_opt PUNICODE_STRING FileName,
    __in BOOLEAN RestartScan
    );

// See ZwQueryDirectoryFile on MSDN: https://msdn.microsoft.com/en-us/library/windows/hardware/ff556633(v=vs.85).aspx
NTSTATUS NTAPI Detoured_ZwQueryDirectoryFile(
    __in HANDLE FileHandle,
    __in_opt HANDLE Event,
    __in_opt PIO_APC_ROUTINE ApcRoutine,
    __in_opt PVOID ApcContext,
    __out PIO_STATUS_BLOCK IoStatusBlock,
    __out_bcount(Length) PVOID FileInformation,
    __in ULONG Length,
    __in FILE_INFORMATION_CLASS FileInformationClass,
    __in BOOLEAN ReturnSingleEntry,
    __in_opt PUNICODE_STRING FileName,
    __in BOOLEAN RestartScan
);

// See ZwSetInformationFile on MSDN. Search for it on the Web.
// https://msdn.microsoft.com/en-us/library/windows/hardware/ff567096(v=vs.85).aspx
NTSTATUS NTAPI Detoured_ZwSetInformationFile(
    _In_  HANDLE                 FileHandle,
    _Out_ PIO_STATUS_BLOCK       IoStatusBlock,
    _In_  PVOID                  FileInformation,
    _In_  ULONG                  Length,
    _In_  FILE_INFORMATION_CLASS FileInformationClass
    );

// See NtCreateFile on MSDN: https://msdn.microsoft.com/en-us/library/bb432380(v=vs.85).aspx
NTSTATUS NTAPI Detoured_NtCreateFile(
    __out PHANDLE FileHandle,
    __in ACCESS_MASK DesiredAccess,
    __in POBJECT_ATTRIBUTES ObjectAttributes,
    __out PIO_STATUS_BLOCK IoStatusBlock,
    __in_opt PLARGE_INTEGER AllocationSize,
    __in ULONG FileAttributes,
    __in ULONG ShareAccess,
    __in ULONG CreateDisposition,
    __in ULONG CreateOptions,
    __in_opt PVOID EaBuffer,
    __in ULONG EaLength
    );

// See NtOpenFile on MSDN: https://msdn.microsoft.com/en-us/library/bb432381(v=vs.85).aspx
NTSTATUS NTAPI Detoured_NtOpenFile(
    __out PHANDLE FileHandle,
    __in ACCESS_MASK DesiredAccess,
    __in POBJECT_ATTRIBUTES ObjectAttributes,
    __out PIO_STATUS_BLOCK IoStatusBlock,
    __in ULONG ShareAccess,
    __in ULONG OpenOptions
    );

// See ZwCreateFile on MSDN: https://msdn.microsoft.com/en-us/library/bb432380(v=vs.85).aspx
NTSTATUS NTAPI Detoured_ZwCreateFile(
    __out PHANDLE FileHandle,
    __in ACCESS_MASK DesiredAccess,
    __in POBJECT_ATTRIBUTES ObjectAttributes,
    __out PIO_STATUS_BLOCK IoStatusBlock,
    __in_opt PLARGE_INTEGER AllocationSize,
    __in ULONG FileAttributes,
    __in ULONG ShareAccess,
    __in ULONG CreateDisposition,
    __in ULONG CreateOptions,
    __in_opt PVOID EaBuffer,
    __in ULONG EaLength
);

// See ZwOpenFile on MSDN: https://msdn.microsoft.com/en-us/library/bb432381(v=vs.85).aspx
NTSTATUS NTAPI Detoured_ZwOpenFile(
    __out PHANDLE FileHandle,
    __in ACCESS_MASK DesiredAccess,
    __in POBJECT_ATTRIBUTES ObjectAttributes,
    __out PIO_STATUS_BLOCK IoStatusBlock,
    __in ULONG ShareAccess,
    __in ULONG OpenOptions
);

// See NtClose on MSDN: https://msdn.microsoft.com/en-us/library/ms648410(v=vs.85).aspx
NTSTATUS NTAPI Detoured_NtClose(
    __in HANDLE Handle
    );

BOOLEAN NTAPI Detoured_RtlFreeHeap(
    _In_     PVOID HeapHandle,
    _In_opt_ ULONG Flags,
    _In_     PVOID HeapBase);

PVOID NTAPI Detoured_RtlAllocateHeap(
    _In_ PVOID HeapHandle,
    _In_opt_ ULONG Flags,
    _In_ SIZE_T Size);

PVOID NTAPI Detoured_RtlReAllocateHeap(
    _In_ PVOID HeapHandle,
    _In_ ULONG Flags,
    _In_opt_ PVOID BaseAddress,
    _In_ SIZE_T Size);

LPVOID WINAPI Detoured_VirtualAlloc(
_In_opt_ LPVOID lpAddress,
_In_ SIZE_T dwSize,
_In_ DWORD flAllocationType,
_In_ DWORD flProtect);

/*

// ---------------------------
// TODO:add the following APIs
// ---------------------------

HANDLE WINAPI CreateFileMappingW
HANDLE WINAPI CreateFileMappingA


//
// PROBABLY NOT
//

HANDLE WINAPI CreateIoCompletionPort


//
// LATER (THESE COMPLICATE THINGS)
//

// requires transaction dlls (KtmW32.lib and .h)
HANDLE WINAPI CreateTransaction // neuter transactions

// requires (NtDll.dll and Winternl.h)
NTSTATUS WINAPI NtCreateFile (
NTSTATUS WINAPI NtOpenFile (
NTSTATUS WINAPI ZwCreateFile (
NTSTATUS WINAPI ZwOpenFile (
NTSTATUS WINAPI NtDeleteFile (
NTSTATUS WINAPI NtClose (

// this API doesn't seem to exist (specifically with Ex suffix)
BOOL WINAPI SetFileInformationByHandleEx(    // it is possible to delete files this way

*/
