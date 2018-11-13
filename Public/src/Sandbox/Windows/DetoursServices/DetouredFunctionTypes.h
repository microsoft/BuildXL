// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// --------------------------------------------------------------------

#pragma once

#include <windows.h>
#include <winternl.h>

// ----------------------------------------------------------------------------
// TYPE DEFINITIONS
// ----------------------------------------------------------------------------

//
// Function signatures for detoured functions
//

typedef BOOL (WINAPI *CreateProcessA_t)(
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

typedef BOOL (WINAPI *CreateProcessW_t)(
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

typedef HANDLE (WINAPI *CreateFileW_t)(
    __in     LPCWSTR lpFileName,
    __in     DWORD dwDesiredAccess,
    __in     DWORD dwShareMode,
    __in_opt LPSECURITY_ATTRIBUTES lpSecurityAttributes,
    __in     DWORD dwCreationDisposition,
    __in     DWORD dwFlagsAndAttributes,
    __in_opt HANDLE hTemplateFile
    );

typedef BOOLEAN (NTAPI *RtlFreeHeap_t)(
    _In_     PVOID HeapHandle,
    _In_opt_ ULONG Flags,
    _In_     PVOID HeapBase);

typedef PVOID(NTAPI *RtlAllocateHeap_t)(
    _In_ PVOID HeapHandle,
    _In_opt_ ULONG Flags,
    _In_ SIZE_T Size);

typedef PVOID(NTAPI *RtlReAllocateHeap_t)(
    _In_ PVOID HeapHandle,
    _In_ ULONG Flags,
    _In_opt_ PVOID BaseAddress,
    _In_ SIZE_T Size);

typedef LPVOID(NTAPI *VirtualAlloc_t)(
    _In_opt_ LPVOID lpAddress,
    _In_ SIZE_T dwSize,
    _In_ DWORD flAllocationType,
    _In_ DWORD flProtect);

typedef HANDLE (WINAPI *CreateFileA_t)(
    __in     LPCSTR lpFileName,
    __in     DWORD dwDesiredAccess,
    __in     DWORD dwShareMode,
    __in_opt LPSECURITY_ATTRIBUTES lpSecurityAttributes,
    __in     DWORD dwCreationDisposition,
    __in     DWORD dwFlagsAndAttributes,
    __in_opt HANDLE hTemplateFile
    );

typedef BOOL (WINAPI *CloseHandle_t)(
    __in     HANDLE handle
    );

typedef BOOL (WINAPI *GetVolumePathNameW_t)(
    __in                          LPCWSTR lpszFileName,
    __out_ecount(cchBufferLength) LPWSTR lpszVolumePathName,
    __in                          DWORD cchBufferLength
    );

typedef DWORD (WINAPI *GetFileAttributesW_t)(
    __in LPCWSTR lpFileName
    );

typedef DWORD (WINAPI *GetFileAttributesA_t)(
    __in LPCSTR lpFileName
    );

typedef BOOL (WINAPI *GetFileAttributesExA_t)(
    __in  LPCSTR lpFileName,
    __in  GET_FILEEX_INFO_LEVELS fInfoLevelId,
    __out LPVOID lpFileInformation
    );

typedef BOOL (WINAPI *GetFileAttributesExW_t)(
    __in  LPCWSTR lpFileName,
    __in  GET_FILEEX_INFO_LEVELS fInfoLevelId,
    __out LPVOID lpFileInformation
    );

typedef BOOL (WINAPI *CopyFileW_t)(
    __in  LPCWSTR lpExistingFileName,
    __in  LPCWSTR lpNewFileName,
    __in  BOOL bFailIfExists
    );

typedef BOOL (WINAPI *CopyFileA_t)(
    __in  LPCSTR lpExistingFileName,
    __in  LPCSTR lpNewFileName,
    __in  BOOL bFailIfExists
    );

typedef BOOL (WINAPI *CopyFileExW_t)(
    __in      LPCWSTR lpExistingFileName,
    __in      LPCWSTR lpNewFileName,
    __in_opt  LPPROGRESS_ROUTINE lpProgressRoutine,
    __in_opt  LPVOID lpData,
    __in_opt  LPBOOL pbCancel,
    __in      DWORD dwCopyFlags
    );

typedef BOOL (WINAPI *CopyFileExA_t)(
    __in      LPCSTR lpExistingFileName,
    __in      LPCSTR lpNewFileName,
    __in_opt  LPPROGRESS_ROUTINE lpProgressRoutine,
    __in_opt  LPVOID lpData,
    __in_opt  LPBOOL pbCancel,
    __in      DWORD dwCopyFlags
    );

typedef BOOL (WINAPI *MoveFileW_t)(
    __in  LPCWSTR lpExistingFileName,
    __in  LPCWSTR lpNewFileName
    );

typedef BOOL (WINAPI *MoveFileA_t)(
    __in  LPCSTR lpExistingFileName,
    __in  LPCSTR lpNewFileName
    );

typedef BOOL (WINAPI *MoveFileExW_t)(
    __in      LPCWSTR lpExistingFileName,
    __in_opt  LPCWSTR lpNewFileName,
    __in      DWORD dwFlags
    );

typedef BOOL (WINAPI *MoveFileExA_t)(
    __in      LPCSTR lpExistingFileName,
    __in_opt  LPCSTR lpNewFileName,
    __in      DWORD dwFlags
    );

typedef BOOL (WINAPI *MoveFileWithProgressW_t)(
    __in      LPCWSTR lpExistingFileName,
    __in_opt  LPCWSTR lpNewFileName,
    __in_opt  LPPROGRESS_ROUTINE lpProgressRoutine,
    __in_opt  LPVOID lpData,
    __in      DWORD dwFlags
    );

typedef BOOL (WINAPI *MoveFileWithProgressA_t)(
    __in      LPCSTR lpExistingFileName,
    __in_opt  LPCSTR lpNewFileName,
    __in_opt  LPPROGRESS_ROUTINE lpProgressRoutine,
    __in_opt  LPVOID lpData,
    __in      DWORD dwFlags
    );

typedef BOOL (WINAPI *ReplaceFileW_t)(
    __in        LPCWSTR lpReplacedFileName,
    __in        LPCWSTR lpReplacementFileName,
    __in_opt    LPCWSTR lpBackupFileName,
    __in        DWORD dwReplaceFlags,
    __reserved  LPVOID lpExclude,
    __reserved  LPVOID lpReserved
    );

typedef BOOL (WINAPI *ReplaceFileA_t)(
    __in        LPCSTR lpReplacedFileName,
    __in        LPCSTR lpReplacementFileName,
    __in_opt    LPCSTR lpBackupFileName,
    __in        DWORD dwReplaceFlags,
    __reserved  LPVOID lpExclude,
    __reserved  LPVOID lpReserved
    );

typedef BOOL (WINAPI *DeleteFileW_t)(
    __in LPCWSTR lpFileName
    );

typedef BOOL (WINAPI *DeleteFileA_t)(
    __in LPCSTR lpFileName
    );


typedef BOOL (WINAPI *CreateHardLinkW_t)(
    __in        LPCWSTR lpFileName,
    __in        LPCWSTR lpExistingFileName,
    __reserved  LPSECURITY_ATTRIBUTES lpSecurityAttributes
    );

typedef BOOL (WINAPI *CreateHardLinkA_t)(
    __in        LPCSTR lpFileName,
    __in        LPCSTR lpExistingFileName,
    __reserved  LPSECURITY_ATTRIBUTES lpSecurityAttributes
    );

typedef BOOLEAN (WINAPI *CreateSymbolicLinkW_t)(
    __in  LPCWSTR lpSymlinkFileName,
    __in  LPCWSTR lpTargetFileName,
    __in  DWORD dwFlags
    );

typedef BOOLEAN (WINAPI *CreateSymbolicLinkA_t)(
    __in  LPCSTR lpSymlinkFileName,
    __in  LPCSTR lpTargetFileName,
    __in  DWORD dwFlags
    );

typedef HANDLE (WINAPI *FindFirstFileW_t)(
    __in   LPCWSTR lpFileName,
    __out  LPWIN32_FIND_DATAW lpFindFileData
    );

typedef HANDLE (WINAPI *FindFirstFileA_t)(
    __in   LPCSTR lpFileName,
    __out  LPWIN32_FIND_DATAA lpFindFileData
    );

typedef HANDLE (WINAPI *FindFirstFileExW_t)(
    __in        LPCWSTR lpFileName,
    __in        FINDEX_INFO_LEVELS fInfoLevelId,
    __out       LPVOID lpFindFileData,
    __in        FINDEX_SEARCH_OPS fSearchOp,
    __reserved  LPVOID lpSearchFilter,
    __in        DWORD dwAdditionalFlags
    );

typedef HANDLE (WINAPI *FindFirstFileExA_t)(
    __in        LPCSTR lpFileName,
    __in        FINDEX_INFO_LEVELS fInfoLevelId,
    __out       LPVOID lpFindFileData,
    __in        FINDEX_SEARCH_OPS fSearchOp,
    __reserved  LPVOID lpSearchFilter,
    __in        DWORD dwAdditionalFlags
    );

typedef BOOL (WINAPI *FindNextFileW_t)(
    __in   HANDLE hFindFile,
    __out  LPWIN32_FIND_DATAW lpFindFileData
    );

typedef BOOL (WINAPI *FindNextFileA_t)(
    __in   HANDLE hFindFile,
    __out  LPWIN32_FIND_DATAA lpFindFileData
    );

typedef BOOL(WINAPI *FindClose_t)(
    __in   HANDLE hFindFile
    );

typedef BOOL (WINAPI *GetFileInformationByHandleEx_t)(
    __in   HANDLE hFile,
    __in   FILE_INFO_BY_HANDLE_CLASS FileInformationClass,
    __out  LPVOID lpFileInformation,
    __in   DWORD dwBufferSize
    );

typedef BOOL(WINAPI *GetFileInformationByHandle_t)(
    __in   HANDLE hFile,
    __out  LPBY_HANDLE_FILE_INFORMATION lpFileInformation
    );

typedef BOOL (WINAPI *SetFileInformationByHandle_t)(
    __in  HANDLE hFile,
    __in  FILE_INFO_BY_HANDLE_CLASS FileInformationClass,
    __in  LPVOID lpFileInformation,
    __in  DWORD dwBufferSize
    );

typedef HANDLE (WINAPI *OpenFileMappingW_t)(
    __in  DWORD dwDesiredAccess,
    __in  BOOL bInheritHandle,
    __in  LPCWSTR lpName
    );

typedef HANDLE (WINAPI *OpenFileMappingA_t)(
    __in  DWORD dwDesiredAccess,
    __in  BOOL bInheritHandle,
    __in  LPCSTR lpName
    );

typedef UINT (WINAPI *GetTempFileNameW_t)(
    __in   LPCWSTR lpPathName,
    __in   LPCWSTR lpPrefixString,
    __in   UINT uUnique,
    __out  LPWSTR lpTempFileName
    );

typedef UINT (WINAPI *GetTempFileNameA_t)(
    __in   LPCSTR lpPathName,
    __in   LPCSTR lpPrefixString,
    __in   UINT uUnique,
    __out  LPSTR lpTempFileName
    );

typedef BOOL (WINAPI *CreateDirectoryW_t)(
    __in      LPCWSTR lpPathName,
    __in_opt  LPSECURITY_ATTRIBUTES lpSecurityAttributes
    );

typedef BOOL (WINAPI *CreateDirectoryA_t)(
    __in      LPCSTR lpPathName,
    __in_opt  LPSECURITY_ATTRIBUTES lpSecurityAttributes
    );

typedef BOOL (WINAPI *CreateDirectoryExW_t)(
    __in      LPCWSTR lpTemplateDirectory,
    __in      LPCWSTR lpNewDirectory,
    __in_opt  LPSECURITY_ATTRIBUTES lpSecurityAttributes
    );

typedef BOOL (WINAPI *CreateDirectoryExA_t)(
    __in      LPCSTR lpTemplateDirectory,
    __in      LPCSTR lpNewDirectory,
    __in_opt  LPSECURITY_ATTRIBUTES lpSecurityAttributes
    );

typedef BOOL (WINAPI *RemoveDirectoryW_t)(
    __in  LPCWSTR lpPathName
    );

typedef BOOL (WINAPI *RemoveDirectoryA_t)(
    __in  LPCSTR lpPathName
    );

typedef BOOL (WINAPI *DecryptFileW_t)(
    __in        LPCWSTR lpFileName,
    __reserved  DWORD dwReserved
    );

typedef BOOL (WINAPI *DecryptFileA_t)(
    __in        LPCSTR lpFileName,
    __reserved  DWORD dwReserved
    );

typedef BOOL (WINAPI *EncryptFileW_t)(
    __in  LPCWSTR lpFileName
    );

typedef BOOL (WINAPI *EncryptFileA_t)(
    __in  LPCSTR lpFileName
    );

typedef DWORD (WINAPI *OpenEncryptedFileRawW_t)(
    __in   LPCWSTR lpFileName,
    __in   ULONG ulFlags,
    __out  PVOID *pvContext
    );

typedef DWORD (WINAPI *OpenEncryptedFileRawA_t)(
    __in   LPCSTR lpFileName,
    __in   ULONG ulFlags,
    __out  PVOID *pvContext
    );

typedef HANDLE (WINAPI *OpenFileById_t)(
    __in      HANDLE hFile,
    __in      LPFILE_ID_DESCRIPTOR lpFileID,
    __in      DWORD dwDesiredAccess,
    __in      DWORD dwShareMode,
    __in_opt  LPSECURITY_ATTRIBUTES lpSecurityAttributes,
    __in      DWORD dwFlags
    );

typedef DWORD(WINAPI *GetFinalPathNameByHandleW_t)(
    __in  HANDLE hFile,
    __out LPTSTR lpszFilePath,
    __in  DWORD cchFilePath,
    __in  DWORD dwFlags
    );

typedef DWORD(WINAPI *GetFinalPathNameByHandleA_t)(
    __in  HANDLE hFile,
    __out LPSTR lpszFilePath,
    __in  DWORD cchFilePath,
    __in  DWORD dwFlags
    );

typedef NTSTATUS (NTAPI *NtQueryDirectoryFile_t)(
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

typedef NTSTATUS(NTAPI *ZwQueryDirectoryFile_t)(
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

typedef NTSTATUS(NTAPI *NtCreateFile_t)(
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

typedef NTSTATUS(NTAPI *NtOpenFile_t)(
    __out PHANDLE FileHandle,
    __in ACCESS_MASK DesiredAccess,
    __in POBJECT_ATTRIBUTES ObjectAttributes,
    __out PIO_STATUS_BLOCK IoStatusBlock,
    __in ULONG ShareAccess,
    __in ULONG OpenOptions
    );

typedef NTSTATUS(NTAPI *ZwCreateFile_t)(
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

typedef NTSTATUS(NTAPI *ZwOpenFile_t)(
    __out PHANDLE FileHandle,
    __in ACCESS_MASK DesiredAccess,
    __in POBJECT_ATTRIBUTES ObjectAttributes,
    __out PIO_STATUS_BLOCK IoStatusBlock,
    __in ULONG ShareAccess,
    __in ULONG OpenOptions
    );

typedef NTSTATUS(NTAPI *ZwSetInformationFile_t)(
    _In_  HANDLE                 FileHandle,
    _Out_ PIO_STATUS_BLOCK       IoStatusBlock,
    _In_  PVOID                  FileInformation,
    _In_  ULONG                  Length,
    _In_  FILE_INFORMATION_CLASS FileInformationClass
    );

typedef NTSTATUS(NTAPI *NtClose_t)(
    __in HANDLE Handle
    );
