// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma once

#include <unordered_set>
#include <cwctype>
#include <algorithm>
#include "DataTypes.h"

// Case-insensitive equality for wstrings
struct CaseInsensitiveStringComparer : public std::binary_function<std::wstring, std::wstring, bool> {
    bool operator()(const std::wstring& lhs, const std::wstring& rhs) const {
        if (lhs.length() == rhs.length()) {
            return std::equal(rhs.begin(), rhs.end(), lhs.begin(),
                [](const wchar_t a, const wchar_t b) { return towlower(a) == towlower(b); });
        }
        else {
            return false;
        }
    }
};

// Case-insensitive hasher for wstrings
struct CaseInsensitiveStringHasher {
    size_t operator()(const std::wstring& str) const {
        std::wstring lowerstr(str);
        std::transform(lowerstr.begin(), lowerstr.end(), lowerstr.begin(), std::towlower);

        return std::hash<std::wstring>()(lowerstr);
    }
};

// Tries to mimic the CreateProcess logic by identifying the image name based on the application
// name and command line for a process
// See https://docs.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessa
std::wstring GetImageName(_In_opt_ LPCWSTR lpApplicationName, _In_opt_ LPWSTR lpCommandLine);

// First it tries with the candidate path, afterwards by appending '.exe' to it
bool TryFindImage(_In_ std::wstring candidatePath, _Out_opt_ std::wstring& imageName);

// Resolves the candidate path into an absolute path and double checks that the path exists on disk
bool IsPathToImage(_In_ std::wstring candidatePath, _Out_opt_ std::wstring& imageName);