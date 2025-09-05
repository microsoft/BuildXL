// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef MANIFEST_ITERATOR_H
#define MANIFEST_ITERATOR_H

#include "DataTypes.h"
#include <queue>
#include <unordered_map>

/**
 * Iterator for traversing the manifest tree-like records.
 */
class ManifestIterator {
public:
    /** Constructor, taking a root manifest record to iterate over. */
    ManifestIterator(const ManifestRecord * manifest);
    /** Move to the next record in the manifest. Returns true if there is a next record. */
    bool MoveNext();
    /** Get the current record in the manifest. */
    const ManifestRecord *GetCurrent() const;
    /** Get the absolute path of the current record, starting from the provided root. The returned path always ends with a separator. */
    std::string GetCurrentPath() const;

private:
    std::queue<const ManifestRecord *> m_queue;
    // Records do not store a back pointer to the parent, but we need it to facilitate reconstructing the full path. 
    // Keep a parent map: child -> parent (root maps to nullptr) as we visit the manifest tree
    std::unordered_map<const ManifestRecord*, const ManifestRecord*> m_parent;
};

#endif