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