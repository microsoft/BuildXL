// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "FileAccessManifestParser.hpp"

// -----------------------------------------------------------------------------
// Note: UNIX_ROOT_SENTINEL must match UnixPathRootSentinel from
//       HierarchicalNameTable.cs
// -----------------------------------------------------------------------------
const char *UNIX_ROOT_SENTINEL = "";

const int UNIX_ROOT_SENTINEL_HASH =
    HashPath(UNIX_ROOT_SENTINEL, strlen(UNIX_ROOT_SENTINEL));

bool WantsWriteAccess(DWORD access)
{
    return (access & (GENERIC_WRITE |
                      MACOS_DELETE |
                      FILE_WRITE_DATA |
                      FILE_WRITE_ATTRIBUTES |
                      FILE_WRITE_EA |
                      FILE_APPEND_DATA)) != 0;
}

bool WantsReadAccess(DWORD access)
{
    return (access & (GENERIC_READ |
                      FILE_READ_ATTRIBUTES |
                      FILE_READ_DATA |
                      FILE_READ_EA)) != 0;
}

bool WantsReadOnlyAccess(DWORD access)
{
    return WantsReadAccess(access) && !WantsWriteAccess(access);
}

RequestedAccess GetRequestedAccess(DWORD desiredAccess)
{
    RequestedAccess reqAccess = (WantsWriteAccess(desiredAccess) ?
                                 RequestedAccess::Write : RequestedAccess::None);

    reqAccess = reqAccess | (WantsReadAccess(desiredAccess) ?
                             RequestedAccess::Read : RequestedAccess::None);

    return reqAccess;
}

// Returns the lenmgth of the skipped-over string.
uint32_t SkipOverCharArray(const BYTE *&cursor)
{
    uint32_t len = *((uint32_t *)(cursor));
    cursor += sizeof(uint32_t);
    // skip over the path (don't care); chars in C# are 2 bytes
    cursor += sizeof(char16_t) *len;
    return len;
}

inline uint32_t ParseUint32(const BYTE *&cursor)
{
    uint32_t i = *(uint32_t*)(cursor);
    cursor += sizeof(uint32_t);
    return i;
}

const char *CheckValidUnixManifestTreeRoot(PCManifestRecord node)
{
    // empty manifest is ok
    if (node->BucketCount == 0)
    {
        return nullptr;
    }

    // otherwise, there must be exactly one root node corresponding to the unix root sentinel '/'
    // (see UnixPathRootSentinel from HierarchicalNameTable.cs)
    if (node->BucketCount != 1)
    {
        return "Root manifest node is expected to have exactly one child (corresponding to the unix root sentinel: '/')";
    }

    if (node->GetChildRecord(0)->Hash != UNIX_ROOT_SENTINEL_HASH)
    {
        return "Wrong hash code for the unix root sentinel node";
    }

    return nullptr;
}

bool FileAccessManifestParseResult::init(const BYTE *payload, size_t payloadSize)
{
    if (payloadSize == 0 || payload == nullptr) return true;

    const BYTE *payloadCursor = payload;

    do
    {
        debugFlag_ = ParseAndAdvancePointer<PCManifestDebugFlag>(payloadCursor);
        if (HasErrors()) continue;

        injectionTimeoutFlag_ = ParseAndAdvancePointer<PCManifestInjectionTimeout>(payloadCursor);
        if (HasErrors()) continue;

        manifestTranslatePathsStrings_ = ParseAndAdvancePointer<PManifestTranslatePathsStrings>(payloadCursor);
        if (HasErrors()) continue;
        uint32_t manifestTranslatePathsSize = ParseUint32(payloadCursor);
        for (uint32_t i = 0; i < manifestTranslatePathsSize; i++)
        {
            SkipOverCharArray(payloadCursor); // 'from' path
            SkipOverCharArray(payloadCursor); // 'to' path
        }

        ParseAndAdvancePointer<PManifestInternalDetoursErrorNotificationFileString>(payloadCursor);
        if (HasErrors()) continue;

        SkipOverCharArray(payloadCursor); // error log path

        flags_ = ParseAndAdvancePointer<PCManifestFlags>(payloadCursor);
        if (HasErrors()) continue;

        extraFlags_ = ParseAndAdvancePointer<PCManifestExtraFlags>(payloadCursor);
        if (HasErrors()) continue;

        pipId_ = ParseAndAdvancePointer<PCManifestPipId>(payloadCursor);
        if (HasErrors()) continue;

        report_ = ParseAndAdvancePointer<PCManifestReport>(payloadCursor);
        if (HasErrors()) continue;

        dllBlock_ = ParseAndAdvancePointer<PCManifestDllBlock>(payloadCursor);
        if (HasErrors()) continue;

        shim_ = ParseAndAdvancePointer<PCManifestSubstituteProcessExecutionShim>(payloadCursor);
        if (HasErrors()) continue;
        uint32_t shimPathLength = SkipOverCharArray(payloadCursor);  // SubstituteProcessExecutionShimPath
        if (shimPathLength > 0)
        {
            uint32_t numProcessMatches = ParseUint32(payloadCursor);
            for (uint32_t i = 0; i < numProcessMatches; i++)
            {
                SkipOverCharArray(payloadCursor); // 'ProcessName'
                SkipOverCharArray(payloadCursor); // 'ArgumentMatch'
            }
        }

        root_ = Parse<PCManifestRecord>(payloadCursor);
        error_ = root_->CheckValid();
        if (HasErrors()) continue;

        error_ = CheckValidUnixManifestTreeRoot(root_);
    } while(false);
    
    return !HasErrors();
}

// Debugging helper
void FileAccessManifestParseResult::PrintManifestTree(PCManifestRecord node,
                                                      const int indent,
                                                      const int index)
{
    PathChar indentStr[indent+1];
    indentStr[indent] = L'\0';
    for (int i = 0; i < indent; i++) indentStr[i] = L' ';

    printf("| %s [%d] '%s' (cone policy = %#x, node policy = %#x)\n", 
           indentStr, 
           index, 
           node->GetPartialPath(), 
           node->GetConePolicy() & FileAccessPolicy_ReportAccess, 
           node->GetNodePolicy() & FileAccessPolicy_ReportAccess);

    for (int i = 0; i < node->BucketCount; i++)
    {
        PCManifestRecord child = node->GetChildRecord(i);
        if (child == nullptr) continue;
        PrintManifestTree(child, indent + 2, i);
    }
}
