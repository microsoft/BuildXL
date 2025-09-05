// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <algorithm>
#include <filesystem>
#include "ManifestIterator.h"
#include "DataTypes.h"

// The system-wide preferred path separator
static std::string preferred_separator(1, std::filesystem::path::preferred_separator);

ManifestIterator::ManifestIterator(const ManifestRecord * manifest) {
    assert(manifest != nullptr);
    m_queue.emplace(manifest);
    m_parent.emplace(manifest, nullptr);
}

bool ManifestIterator::MoveNext() {
    if (m_queue.empty())
    {
        return false;
    }

    // Get the current record and remove it from the queue
    const PCManifestRecord current = m_queue.front();
    m_queue.pop();

    // Add all child records to the queue
    for (int i = 0; i < current->BucketCount; i++) {
        PCManifestRecord child = current->GetChildRecord(i);
        if (child != nullptr) {
            m_queue.emplace(child);
            m_parent.emplace(child, current);
        }
    }

    return !m_queue.empty();
}

const ManifestRecord * ManifestIterator::GetCurrent() const {
    return m_queue.front();
}

std::string ManifestIterator::GetCurrentPath() const {
    std::string result;
    if (m_queue.empty()) {
        return result;
    }

    // walk up parents
    const ManifestRecord* node = m_queue.front();
    while (node != nullptr) {
        // Find the parent. If the parent is missing, stop. The root of the manifest
        // has a null path
        auto it = m_parent.find(node);
        const ManifestRecord* parent = (it != m_parent.end()) ? it->second : nullptr;
        if (parent == nullptr) {
            break;
        }

        // We build the string backwards, since append is cheaper than insert
        result.append(preferred_separator);
        std::string atom = node->GetPartialPath();
        std::reverse(atom.begin(), atom.end());
        result.append(atom);

        node = parent;
    }
     
    // The result is backwards, so reverse it
    std::reverse(result.begin(), result.end());
    
    return result;
}