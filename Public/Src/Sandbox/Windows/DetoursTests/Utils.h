// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma once

#include "stdafx.h"

using namespace std;

typedef NTSTATUS(__stdcall *_NtCreateFile)(
    PHANDLE FileHandle,
    ACCESS_MASK DesiredAccess,
    POBJECT_ATTRIBUTES ObjectAttributes,
    PIO_STATUS_BLOCK IoStatusBlock,
    PLARGE_INTEGER AllocationSize,
    ULONG FileAttributes,
    ULONG ShareAccess,
    ULONG CreateDisposition,
    ULONG CreateOptions,
    PVOID EaBuffer,
    ULONG EaLength);

typedef NTSTATUS(__stdcall *_NtClose)(HANDLE FileHandle);

typedef VOID(__stdcall *_RtlInitUnicodeString)(
    PUNICODE_STRING DestinationString,
    PCWSTR SourceString);

#define InitializeObjectAttributes( i, o, a, r, s ) {    \
      (i)->Length = sizeof( OBJECT_ATTRIBUTES );         \
      (i)->RootDirectory = r;                            \
      (i)->Attributes = a;                               \
      (i)->ObjectName = o;                               \
      (i)->SecurityDescriptor = s;                       \
      (i)->SecurityQualityOfService = NULL;              \
   }

typedef enum _FILE_INFORMATION_CLASS_EXTRA {
    FileFullDirectoryInformation = 2,
    FileBothDirectoryInformation,
    FileBasicInformation,
    FileStandardInformation,
    FileInternalInformation,
    FileEaInformation,
    FileAccessInformation,
    FileNameInformation,
    FileRenameInformation,
    FileLinkInformation,
    FileNamesInformation,
    FileDispositionInformation,
    FilePositionInformation,
    FileFullEaInformation,
    FileModeInformation,
    FileAlignmentInformation,
    FileAllInformation,
    FileAllocationInformation,
    FileEndOfFileInformation,
    FileAlternateNameInformation,
    FileStreamInformation,
    FilePipeInformation,
    FilePipeLocalInformation,
    FilePipeRemoteInformation,
    FileMailslotQueryInformation,
    FileMailslotSetInformation,
    FileCompressionInformation,
    FileObjectIdInformation,
    FileCompletionInformation,
    FileMoveClusterInformation,
    FileQuotaInformation,
    FileReparsePointInformation,
    FileNetworkOpenInformation,
    FileAttributeTagInformation,
    FileTrackingInformation,
    FileIdBothDirectoryInformation,
    FileIdFullDirectoryInformation,
    FileValidDataLengthInformation,
    FileShortNameInformation,
    FileIoCompletionNotificationInformation,
    FileIoStatusBlockRangeInformation,
    FileIoPriorityHintInformation,
    FileSfioReserveInformation,
    FileSfioVolumeInformation,
    FileHardLinkInformation,
    FileProcessIdsUsingFileInformation,
    FileNormalizedNameInformation,
    FileNetworkPhysicalNameInformation,
    FileIdGlobalTxDirectoryInformation,
    FileIsRemoteDeviceInformation,
    FileUnusedInformation,
    FileNumaNodeInformation,
    FileStandardLinkInformation,
    FileRemoteProtocolInformation,
    FileRenameInformationBypassAccessCheck,
    FileLinkInformationBypassAccessCheck,
    FileVolumeNameInformation,
    FileIdInformation,
    FileIdExtdDirectoryInformation,
    FileReplaceCompletionInformation,
    FileHardLinkFullIdInformation,
    FileIdExtdBothDirectoryInformation,
    FileDispositionInformationEx,
    FileRenameInformationEx,
    FileRenameInformationExBypassAccessCheck,
    FileDesiredStorageClassInformation,
    FileStatInformation,
    FileMemoryPartitionInformation,
    FileStatLxInformation,
    FileCaseSensitiveInformation,
    FileLinkInformationEx,
    FileLinkInformationExBypassAccessCheck,
    FileStorageReserveIdInformation,
    FileCaseSensitiveInformationForceAccessCheck,
    FileMaximumInformation
} FILE_INFORMATION_CLASS_EXTRA, * PFILE_INFORMATION_CLASS_EXTRA;

typedef struct _FILE_LINK_INFORMATION {
    BOOLEAN ReplaceIfExists;
    HANDLE  RootDirectory;
    ULONG   FileNameLength;
    WCHAR   FileName[1];
} FILE_LINK_INFORMATION, * PFILE_LINK_INFORMATION;

typedef struct _FILE_LINK_INFORMATION_EX {
    union {
        BOOLEAN ReplaceIfExists;
        ULONG Flags;
    };
    HANDLE  RootDirectory;
    ULONG   FileNameLength;
    WCHAR   FileName[1];
} FILE_LINK_INFORMATION_EX, * PFILE_LINK_INFORMATION_EX;

extern "C" {
    NTSTATUS NTAPI ZwSetInformationFile(
        _In_  HANDLE                 FileHandle,
        _Out_ PIO_STATUS_BLOCK       IoStatusBlock,
        _In_  PVOID                  FileInformation,
        _In_  ULONG                  Length,
        _In_  FILE_INFORMATION_CLASS FileInformationClass);

    NTSTATUS NTAPI ZwCreateFile(
        _Out_    PHANDLE            FileHandle,
        _In_     ACCESS_MASK        DesiredAccess,
        _In_     POBJECT_ATTRIBUTES ObjectAttributes,
        _Out_    PIO_STATUS_BLOCK   IoStatusBlock,
        _In_opt_ PLARGE_INTEGER     AllocationSize,
        _In_     ULONG              FileAttributes,
        _In_     ULONG              ShareAccess,
        _In_     ULONG              CreateDisposition,
        _In_     ULONG              CreateOptions,
        _In_opt_ PVOID              EaBuffer,
        _In_     ULONG              EaLength);

    NTSTATUS NTAPI ZwOpenFile(
        _Out_ PHANDLE            FileHandle,
        _In_  ACCESS_MASK        DesiredAccess,
        _In_  POBJECT_ATTRIBUTES ObjectAttributes,
        _Out_ PIO_STATUS_BLOCK   IoStatusBlock,
        _In_  ULONG              ShareAccess,
        _In_  ULONG              OpenOptions);

    NTSTATUS NTAPI ZwClose(_In_ HANDLE FileHandle);
}

BOOL SetRenameFileByHandle(HANDLE hFile, const wstring& target, bool correctFileNameLength);
NTSTATUS ZwSetRenameFileByHandle(HANDLE hFile, LPCWSTR targetName, FILE_INFORMATION_CLASS_EXTRA fileInfoClass);
BOOL SetFileDispositionByHandle(HANDLE hFile, FILE_INFO_BY_HANDLE_CLASS fileInfoClass);
NTSTATUS ZwSetFileDispositionByHandle(HANDLE hFile, FILE_INFORMATION_CLASS_EXTRA fileInfoClass);

_NtCreateFile GetNtCreateFile();
_NtClose GetNtClose();
_RtlInitUnicodeString GetRtlInitUnicodeString();

bool TryGetFullPath(_In_ LPCWSTR path, _Out_ wstring& fullPath);
bool TryGetNtFullPath(_In_ LPCWSTR path, _Out_ wstring& fullPath);
bool TryGetNtEscapedFullPath(_In_ LPCWSTR path, _Out_ wstring& fullPath);
BOOLEAN TestCreateSymbolicLinkW(_In_ LPCWSTR lpSymlinkFileName, _In_ LPCWSTR lpTargetFileName, _In_ DWORD dwFlags);
BOOLEAN TestCreateSymbolicLinkA(_In_ LPCSTR lpSymlinkFileName, _In_ LPCSTR lpTargetFileName, _In_ DWORD dwFlags);