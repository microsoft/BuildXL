// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "FilesCheckedForAccess.h"
#include "string.h"

FilesCheckedForAccess::FilesCheckedForAccess()
{
    InitializeCriticalSection(&m_lock);
}

bool FilesCheckedForAccess::TryRegisterPath(const CanonicalizedPath& path) {
    std::pair<std::unordered_set<std::wstring>::iterator, bool> result;
    
    EnterCriticalSection(&m_lock);
    result = m_pathSet.insert(path.GetPathString());
    LeaveCriticalSection(&m_lock);

    return result.second;
}

bool FilesCheckedForAccess::IsRegistered(const CanonicalizedPath& path) {
    std::unordered_set<std::wstring>::iterator result;
    
    EnterCriticalSection(&m_lock);
    result = m_pathSet.find(path.GetPathString());
    LeaveCriticalSection(&m_lock);

    return (result != m_pathSet.end());
}

FilesCheckedForAccess* g_filesCheckedForAccess = NULL;

void InitializeFilesCheckedForWriteAccesses() {
    assert(g_filesCheckedForAccess == NULL);
    g_filesCheckedForAccess = new FilesCheckedForAccess();
}

FilesCheckedForAccess* GetGlobalFilesCheckedForAccesses() {
    assert(g_filesCheckedForAccess != NULL);
    return g_filesCheckedForAccess;
}
