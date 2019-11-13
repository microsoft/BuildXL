// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma once

#include "CanonicalizedPath.h"
#include "UtilityHelpers.h"
#include <unordered_set>
#include <cwctype>
#include <algorithm>

// Keeps a set of case-insensitive paths that were checked for access 
// All operations are thread-safe
class FilesCheckedForAccess {
public:
    FilesCheckedForAccess();
    // Tries to register that a given path was checked for access
    // Returns whether the path was not registered before
    bool TryRegisterPath(const CanonicalizedPath& path);
    
    // Returns whether the given path is registered.
    bool IsRegistered(const CanonicalizedPath& path);

private:
    std::unordered_set<std::wstring, CaseInsensitiveStringHasher, CaseInsensitiveStringComparer> m_pathSet;
    CRITICAL_SECTION m_lock;
};

// Sets up structures for recording write access checks.
void InitializeFilesCheckedForWriteAccesses();

// Returns a pointer to the global instance of FilesCheckedForWriteAccess
// Assumes InitializeFilesCheckedForWriteAccesses() has been called
FilesCheckedForAccess* GetGlobalFilesCheckedForAccesses();
