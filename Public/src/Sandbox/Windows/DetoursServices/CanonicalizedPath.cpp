// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// --------------------------------------------------------------------

#include "CanonicalizedPath.h"

// Applies GetFullPathnameW to 'path'. This function should not be used on \\?\ or \??\ style paths.
static DWORD GetFullPath(__in PCWSTR path, std::wstring& fullPath)
{
    // First, we try with a fixed-sized buffer, which should be good enough for all practical cases

    WCHAR wszBuffer[MAX_PATH];
    DWORD nBufferLength = sizeof(wszBuffer) / sizeof(WCHAR);

    DWORD result = GetFullPathNameW(path, nBufferLength, wszBuffer, NULL);

    if (result == 0)
    {
        return GetLastError();
    }

    if (result < nBufferLength)
    {
        // The buffer was big enough. The return value indicates the length of the full path, NOT INCLUDING the terminating null character.
        // http://msdn.microsoft.com/en-us/library/windows/desktop/aa364963(v=vs.85).aspx
        fullPath = std::wstring(wszBuffer, static_cast<size_t>(result));
    }
    else
    {
        // Second, if that buffer wasn't big enough, we try again with a dynamically allocated buffer with sufficient size

        // Note that in this case, the return value indicates the required buffer length, INCLUDING the terminating null character.
        // http://msdn.microsoft.com/en-us/library/windows/desktop/aa364963(v=vs.85).aspx
        unique_ptr<wchar_t[]> buffer(new wchar_t[result]);
        assert(buffer.get());

        DWORD result2 = GetFullPathNameW(path, result, buffer.get(), NULL);

        if (result2 == 0)
        {
            return GetLastError();
        }

        if (result2 < result)
        {
            fullPath = std::wstring(buffer.get(), result2);
        }
        else
        {
            return ERROR_NOT_ENOUGH_MEMORY;
        }
    }

    return ERROR_SUCCESS;
}

CanonicalizedPath CanonicalizedPath::Canonicalize(wchar_t const* noncanonicalPath) {
    PathType pathType;
    std::wstring fullPath;
    if (IsWin32NtPathName(noncanonicalPath)) {
        // Caller is using escape syntax to avoid Win32 interpretation of path.
        // That's actually really good for us.  The text after the prefix is
        // always an absolute path. Note that we must skip calling GetFullPathName here;
        // the kernel's effective algorithm for translating to an NT path is something like 
        //    IsWin32NtPathName(path) ? path : GetFullPathName(path),
        // and in fact GetFullPathName(path) and path aren't always equivalent if IsWin32NtPathName(path).
        pathType = PathType::Win32Nt;
        fullPath = std::wstring(noncanonicalPath);
    }
    else {
        // The path is not a Win32-NT pathname so it is subject to GetFullPathName canonicalization by the kernel.
        // So, C:\foo\..\bar becomes C:\bar. But also \\.\C:\foo\..\bar becomes \\.\C:\bar ; note that the local device (\\.\)
        // prefix is preserved. That's fine for reporting (we keep it as m_canonicalizedPath), but for computing special cases
        // and traversing the manifest tree, we should further canonicalize to the plain C:\bar (C: is in the tree, \\.\ isn't understood). 
        // Note that even non-drive-letter devices like \\.\nul, \\.\Harddisk0Partition1, etc. can safely become nul and Harddisk0Partition1 respectively; 
        // imagine the manifest tree root as implicitly \??\ (the session's DosDevices namespace).

        DWORD error = GetFullPath(noncanonicalPath, fullPath);
        if (error != ERROR_SUCCESS) {
            return CanonicalizedPath();
        }

        // Note that GetFullPath("nul") == "\\.\nul" (similar for other classic devices), so we check for the local device type after that step.
        pathType = IsLocalDevicePathName(fullPath.c_str()) ? PathType::LocalDevice : PathType::Win32;
    }

    return CanonicalizedPath(pathType, std::move(fullPath));
}

CanonicalizedPath CanonicalizedPath::Extend(wchar_t const* additionalComponents, size_t* extensionStartIndex) const {
    assert(additionalComponents);
    assert(!IsNull() && m_value);
    while (IsPathSeparator(additionalComponents[0])) {
        additionalComponents++;
    }

    std::wstring extended{};
    extended.reserve(wcslen(additionalComponents) + Length() + 1);
    extended.append(*m_value);

    if (!extended.empty() && !IsPathSeparator(extended.back())) {
        extended.push_back(NT_PATH_SEPARATOR);
    }

    if (extensionStartIndex != nullptr) {
        *extensionStartIndex = extended.size();
    }

    extended.append(additionalComponents);

    return CanonicalizedPath(Type, std::move(extended));
}

wchar_t const* CanonicalizedPath::GetLastComponent() const {
    if (IsNull()) {
        return nullptr;
    }
    else {
        wchar_t const* str = GetPathString();
        size_t lastPathSeparatorOrNullIndex = FindFinalPathSeparator(str);

        if (str[lastPathSeparatorOrNullIndex] == L'\0') {
            return &str[lastPathSeparatorOrNullIndex];
        }
        else {
            return &str[lastPathSeparatorOrNullIndex + 1];
        }
    }
}

CanonicalizedPath CanonicalizedPath::RemoveLastComponent() const {
    assert(!IsNull() && m_value);

    // If the last path separator is at zero-based index N, we want the preceding N characters.
    // If there are no path separators (or a path separator at index 0), we want a zero length string.
    size_t lastSeparatorIndex = FindFinalPathSeparator(m_value->c_str());
    return CanonicalizedPath(Type, m_value->substr(0, lastSeparatorIndex));
}