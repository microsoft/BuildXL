// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// --------------------------------------------------------------------

// Utils.cpp : Defines the utilities needed for the tests.

#include "stdafx.h"

#include "Utils.h"

using namespace std;

_NtCreateFile GetNtCreateFile()
{
    return reinterpret_cast<_NtCreateFile>(reinterpret_cast<void*>(GetProcAddress(GetModuleHandle(L"ntdll.dll"), "NtCreateFile")));
}

_NtClose GetNtClose()
{
    return reinterpret_cast<_NtClose>(reinterpret_cast<void*>(GetProcAddress(GetModuleHandle(L"ntdll.dll"), "NtClose")));
}

_RtlInitUnicodeString GetRtlInitUnicodeString()
{
    return reinterpret_cast<_RtlInitUnicodeString>(reinterpret_cast<void*>(GetProcAddress(GetModuleHandle(L"ntdll.dll"), "RtlInitUnicodeString")));
}

bool TryGetFullPath(_In_ LPCWSTR path, _Out_ wstring& fullPath)
{
    const int BufferSize = 4096;
    WCHAR buffer[BufferSize] = L"";
    WCHAR** lppPart = { NULL };

    DWORD result = GetFullPathNameW(
        path,
        BufferSize,
        buffer,
        lppPart);

    if (result >= BufferSize)
    {
        wprintf(L"TryGetFullPath: Buffer size for '%s' is not enough. Required size is %ld \n", path, result);
        return false;
    }

    if (result != 0)
    {
        fullPath.append(buffer);
        return true;
    }

    wprintf(L"TryGetFullPath: Failed GetFullPathNameW: error: %ld \n", GetLastError());
    return false;
}

bool TryGetNtFullPath(_In_ LPCWSTR path, _Out_ wstring& fullPath)
{
    fullPath.append(L"\\??\\");
    return TryGetFullPath(path, fullPath);
}

bool TryGetNtEscapedFullPath(_In_ LPCWSTR path, _Out_ wstring& fullPath)
{
    fullPath.append(L"\\\\?\\");
    return TryGetFullPath(path, fullPath);
}
