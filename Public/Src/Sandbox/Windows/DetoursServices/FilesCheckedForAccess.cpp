// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "FilesCheckedForAccess.h"
#include "string.h"

FilesCheckedForAccess::FilesCheckedForAccess()
{
}

bool FilesCheckedForAccess::TryRegisterPath(const CanonicalizedPathType& path) {
    const std::unique_lock<std::shared_mutex> lock(m_lock);
#if _WIN32
    auto result = m_pathSet.insert(path.GetPathString());
#else
    auto result = m_pathSet.insert(path);
#endif

    return result.second;
}

bool FilesCheckedForAccess::IsRegistered(const CanonicalizedPathType& path) {
    const std::shared_lock<std::shared_mutex> lock(m_lock);
#if _WIN32
    auto result = m_pathSet.find(path.GetPathString());
#else
    auto result = m_pathSet.find(path);
#endif

    return (result != m_pathSet.end());
}

FilesCheckedForAccess* FilesCheckedForAccess::GetInstance() {
    static FilesCheckedForAccess s_singleton;
    return &s_singleton;
}
