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

// Returns the length of the skipped-over string.
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

uint32_t CreateStringFromUtf16Chars(const BYTE *&payload, char *buffer, int bufferSize)
{
    // NOTE: this function assumes that the string from payload that is being read was encoded in utf16.
    // it also assumes that the characters being parsed falls within the utf8 range.
    uint32_t len = ParseUint32(payload);

    if (len >= bufferSize - 1)
    {
        // truncate the value if it's bigger than the provided buffer
        len = bufferSize;
    }

    memset(buffer, 0, bufferSize);

    if (len > 0)
    {
        for (int i = 0; i < len; i++)
        {
            // this is a narrowing cast
            buffer[i] = static_cast<char>(payload[i * sizeof(char16_t)]);
        }
        buffer[len] = '\0';
        payload += sizeof(char16_t) * len;
    }

    return len;
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

    unsigned int expectedHash = HashPath(UNIX_ROOT_SENTINEL, 0);
    if (node->GetChildRecord(0)->Hash != expectedHash)
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

        // For now we just skip the list of processes to breakaway. TODO: a future implementation may consider these
        // to determine whether to skip reporting accesses for them
        manifestChildProcessesToBreakAwayFromJob_ = ParseAndAdvancePointer<PManifestChildProcessesToBreakAwayFromJob>(payloadCursor);
        if (HasErrors()) continue;

        for (uint32_t i = 0; i < manifestChildProcessesToBreakAwayFromJob_->Count ; i++)
        {
            // CODESYNC: FileAccessManifest.cs :: WriteChildProcessesToBreakAwayFromSandbox
            SkipOverCharArray(payloadCursor); // process name
            SkipOverCharArray(payloadCursor); // requiredCommandLineArgsSubstring
            payloadCursor++; // commandLineArgsSubstringContainmentIgnoreCase
        }

        manifestTranslatePathsStrings_ = ParseAndAdvancePointer<PManifestTranslatePathsStrings>(payloadCursor);
        if (HasErrors()) continue;

        for (uint32_t i = 0; i < manifestTranslatePathsStrings_->Count; i++)
        {
            SkipOverCharArray(payloadCursor); // 'from' path
            SkipOverCharArray(payloadCursor); // 'to' path
        }

        ParseAndAdvancePointer<PManifestInternalDetoursErrorNotificationFileString>(payloadCursor);
        if (HasErrors()) continue;

        // on Unix this does not point to a real path, however to align with the Windows format for the file access manifest we'll re-use this for now
        // this string is encoded in utf16 in the manifest
        CreateStringFromUtf16Chars(payloadCursor, internalDetoursErrorNotificationFile_, NAME_MAX-4);

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
            SkipOverCharArray(payloadCursor);  // SubstituteProcessExecutionPluginDll32Path
            SkipOverCharArray(payloadCursor);  // SubstituteProcessExecutionPluginDll64Path
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
std::string FileAccessManifestParseResult::PrintManifestTree(PCManifestRecord node,
                                                      const int indent,
                                                      const int index)
{
    if (node == nullptr) node = root_;
    PathChar indentStr[indent+1];
    indentStr[indent] = L'\0';
    for (int i = 0; i < indent; i++) indentStr[i] = L' ';

    std::string output("| ");
    output.append(indentStr);
    output.append(" [");
    output.append(std::to_string(index));
    output.append("] '");
    output.append(node->GetPartialPath());
    output.append("' (cone policy = ");
    output.append(std::to_string(node->GetConePolicy() & FileAccessPolicy_ReportAccess));
    output.append(", node policy = ");
    output.append(std::to_string(node->GetNodePolicy() & FileAccessPolicy_ReportAccess));
    output.append(")\n");

    for (int i = 0; i < node->BucketCount; i++)
    {
        PCManifestRecord child = node->GetChildRecord(i);
        if (child == nullptr) continue;
        output.append(PrintManifestTree(child, indent + 2, i));
    }

    return output;
}
