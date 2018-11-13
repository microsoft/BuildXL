// --------------------------------------------------------------------
//  
// Copyright (c) Microsoft Corporation.  All rights reserved.
//  
// --------------------------------------------------------------------

#include "stdafx.h"
#include "PolicySearch.h"
#include "StringOperations.h"

/// GetPartialPathAndRemainder
///
/// Takes a path and trims out the first partial path. Because the contract
/// for this and all functions is not to modify or copy strings, return the
/// length of the partial path, and the caller will use absolutePath along
/// with the return value and treat that as if it were the partial path.
/// Keep in mind that said partial path string is not null-terminated.
///
/// Remainder is the string beginning after the dividing path separator.
///
/// Returns:
///     The length of the partial path, not including the null terminator or path separator.
/// Outputs:
///     absolutePath (unmodified): The partial path (as a prefix of absolutePath), with no path separator.
///     remainder: The remainder of the input string after the partial path has been stripped off.
static size_t GetPartialPathAndRemainder(
    __in  PCPathChar absolutePath,
    __in  size_t absolutePathLength,
    __out PCPathChar& remainder)
{
    assert(absolutePath);
    assert(absolutePathLength == pathlen(absolutePath));
    
    size_t found = 0; // look for a path separator or end of string
    // Skip all the leading PathSeparators.
    // This is needed for the case of network path ("\\foo-server\bar").
    while (IsPathSeparator(absolutePath[found]))
    {
        found++;
    }

    for (; found < absolutePathLength && !(IsPathSeparator(absolutePath[found])); found++);

    remainder = (absolutePath + found);

    if (found < absolutePathLength) {
        assert(IsPathSeparator(remainder[0]) && (remainder[0] != L'\0'));
        // we found a path separator, and we need to increment the remainder past the path separator
        remainder++;
    }
    else {
        // absolutely do not increment past the null terminator
        assert(remainder[0] == L'\0');
    }

    return found;
}

PolicySearchCursor FindFileAccessPolicyInTreeEx(
    __in  PolicySearchCursor const& cursor,
    __in  PCPathChar absolutePath,
    __in  size_t absolutePathLength)
{
    assert(absolutePath);
    assert(absolutePathLength == pathlen(absolutePath));

    assert(cursor.Record != nullptr);
    assert(absolutePath != nullptr);

    // For a truncated cursor, any further search should yield the same policy and remain truncated.
    // One can imagine that below each record, there is a default record for any unmatched path
    // which is an equivalent copy. But instead of realizing those records we just remember that
    // we have begun traversing them.
    if (cursor.SearchWasTruncated) {
        return cursor;
    }

    // Terminal cases: Maybe we can't walk further down the tree, or maybe we've matched all of the path.
    ManifestRecord::BucketCountType numBuckets = cursor.Record->BucketCount;
    bool isLeaf = numBuckets == 0; // we found a leaf, even if there is more path, we have gone as far as we can
    bool endOfPath = absolutePath[0] == 0; // no more path to search, wherever we ended up is the node to consider
    if (isLeaf || endOfPath)
    {
        return PolicySearchCursor(cursor.Record, /*searchWasTruncated*/ !endOfPath);
    }

    // We're now committed to tokenizing a further path component, and trying to find a matching child.

    PCPathChar remainder = NULL;
    size_t partialPathLength = GetPartialPathAndRemainder(absolutePath, absolutePathLength, /*out*/ remainder);
    assert(absolutePath + partialPathLength <= remainder);
    assert(remainder >= absolutePath);
    assert(remainder <= absolutePath + absolutePathLength);

    PCManifestRecord childRecord = NULL;
    bool childFound = cursor.Record->FindChild(absolutePath, partialPathLength, /*out*/ childRecord);
    if (!childFound || childRecord == NULL)
    {
        // There was path to consume, and a chance of finding a child record, but that didn't work.
        // So, this is a third terminal case (but we had to do a bit of work to determine so).
        return PolicySearchCursor(cursor.Record, /*searchWasTruncated*/ true);
    }

    assert(childRecord != NULL);

    // childRecord's partialPath is a prefix of remainder.
    size_t remainderLength = absolutePathLength - (remainder - absolutePath);
    assert(remainderLength == pathlen(remainder));
    // Recursive step: Consume some more of the path, if any. Note that we always recurse with a non-truncated cursor due to the terminal cases above.
    return FindFileAccessPolicyInTreeEx(PolicySearchCursor(childRecord), remainder, remainderLength);
}

#ifdef BUILDXL_NATIVES_LIBRARY
BOOL WINAPI FindFileAccessPolicyInTree(
    __in  ManifestRecord const* record,
    __in  PCPathChar absolutePath,
    __in  size_t absolutePathLength,
	__out FileAccessPolicy& conePolicy,
	__out FileAccessPolicy& nodePolicy,
    __out DWORD& pathId,
    __out USN& expectedUsn)
{
    if (record == nullptr || absolutePath == nullptr) {
        return false;
    }

    PolicySearchCursor newCursor = FindFileAccessPolicyInTreeEx(PolicySearchCursor(record), absolutePath, absolutePathLength);
	conePolicy = newCursor.Record->GetConePolicy();
	nodePolicy = newCursor.Record->GetNodePolicy();
    expectedUsn = newCursor.GetExpectedUsn();
    pathId = newCursor.Record->GetPathId();
    return true;
}
#endif // BUILDXL_NATIVES_LIBRARY

/// FindChild
///
/// Search for the given partial path in the children of the given node.
/// If found, returns the index of the child; otherwise returns false.
__success(return)
bool ManifestRecord::FindChild(
__in  PCPathChar target,
__in  size_t targetLength,
__out PCManifestRecord& child) const
{
    DWORD hash = HashPath(target, targetLength);
    ManifestRecord::BucketCountType numBuckets = this->BucketCount;

    // We are searching a hash-table that has been constructed in FileAccessManifest.cs
    ManifestRecord::BucketCountType index = hash % numBuckets;

    child = this->GetChildRecord(index);
    if (child == nullptr)
    {
        return false;
    }

    if (child->Hash == hash && ArePathsEqual(target, child->GetPartialPath(), targetLength))
    {
        return true;
    }

    if (!this->IsCollisionChainStart(index))
    {
        return false;
    }

    do {
        index = (index + 1) % numBuckets;
        child = this->GetChildRecord(index);

        assert(child);
        if (child->Hash == hash &&
            ArePathsEqual(target, child->GetPartialPath(), targetLength))
        {
            return true;
        }
    } while (this->IsCollisionChainContinuation(index));

    return false;
}
