// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma once

#include "FileAccessHelpers.h"

#if _WIN32
    #include "CanonicalizedPath.h"
    typedef CanonicalizedPath CanonicalizedPathType;
#else // _WIN32
    typedef PCPathChar CanonicalizedPathType;
#endif // _WIN32

#include <unordered_set>
#include <cwctype>
#include <mutex>
#include <shared_mutex>
#include <cassert>
#if _WIN32
    #include "UtilityHelpers.h"
    #include <algorithm>
#endif

// Keeps a set of case-insensitive paths that were checked for access 
// All operations are thread-safe
class FilesCheckedForAccess {
public:
    static FilesCheckedForAccess* GetInstance();

    // Tries to register that a given path was checked for access
    // Returns whether the path was not registered before
    bool TryRegisterPath(const CanonicalizedPathType& path);
    
    // Returns whether the given path is registered.
    bool IsRegistered(const CanonicalizedPathType& path);

private:
    FilesCheckedForAccess();
    FilesCheckedForAccess(const FilesCheckedForAccess&) = delete;
    FilesCheckedForAccess& operator = (const FilesCheckedForAccess&) = delete;

// We only want case insensitive comparisons on Windows
#if _WIN32
    std::unordered_set<std::wstring, CaseInsensitiveStringHasher, CaseInsensitiveStringComparer> m_pathSet;
#else
    std::unordered_set<std::string> m_pathSet;
#endif
    std::shared_mutex m_lock;
};
