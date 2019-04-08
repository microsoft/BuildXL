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

_NtCreateFile GetNtCreateFile();
_NtClose GetNtClose();
_RtlInitUnicodeString GetRtlInitUnicodeString();

bool TryGetFullPath(_In_ LPCWSTR path, _Out_ wstring& fullPath);
bool TryGetNtFullPath(_In_ LPCWSTR path, _Out_ wstring& fullPath);
bool TryGetNtEscapedFullPath(_In_ LPCWSTR path, _Out_ wstring& fullPath);
BOOLEAN TestCreateSymbolicLinkW(_In_ LPCWSTR lpSymlinkFileName, _In_ LPCWSTR lpTargetFileName, _In_ DWORD dwFlags);
BOOLEAN TestCreateSymbolicLinkA(_In_ LPCSTR lpSymlinkFileName, _In_ LPCSTR lpTargetFileName, _In_ DWORD dwFlags);