// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "FileAccessManifest.h"
#include "DataTypes.h"

namespace buildxl {
namespace common {

FileAccessManifest::FileAccessManifest(char *payload, size_t payload_size) {
    payload_ = std::unique_ptr<char []>(payload);
    payload_size_ = payload_size;

    ParseFileAccessManifest();
}

FileAccessManifest::~FileAccessManifest() { }

bool FileAccessManifest::ParseFileAccessManifest() {
    if (payload_size_ == 0) {
        return true;
    }

    size_t offset = 0;

    // CODESYNC: Public/Src/Engine/Processes/FileAccessManifest.cs
    // The parsing order must match the order in FileAccessManifest.GetPayloadBytes
    // Certain parts of the manifest are not used in Unix, so we don't parse them (this will change in the future when the Windows sandbox also uses this code)
    // NOTE: Each of the Parse* functions in this file will advance the offset by the size of the parsed value.

    // 1. Debug Flag
    auto debugFlag = reinterpret_cast<PCManifestDebugFlag>(&(payload_.get()[offset]));
    if (!debugFlag->CheckValidityAndHandleInvalid()) {
        assert(false && "Invalid debug flag");
        return false;
    }

    offset += debugFlag->GetSize();

    // 2. Injection Timeout
    auto injection_timeout_minutes = reinterpret_cast<PCManifestInjectionTimeout>(&(payload_.get()[offset]));
    if (!injection_timeout_minutes->CheckValidityAndHandleInvalid()) {
        assert(false && "Invalid injection timeout");
        return false;
    }
    injection_timeout_minutes_ = static_cast<unsigned long>(injection_timeout_minutes->Flags);
    offset += injection_timeout_minutes->GetSize();

    // 3. Breakaway Child Processes
    auto child_processes_to_break_away_from_job = ParseAndAdvancePointer<PManifestChildProcessesToBreakAwayFromJob>(offset);
    for (uint32_t i = 0; i < child_processes_to_break_away_from_job->Count; i++) {
        std::basic_string<PathChar> process_name;
        ParseUtf16CharArrayToString(offset, process_name);

        if (!process_name.empty()) {
            std::basic_string<PathChar> required_args;
            ParseUtf16CharArrayToString(offset, required_args);

            auto ignore_case = ParseByte(offset) == 1U;

            breakaway_child_processes_.push_back(BreakawayChildProcess(process_name, required_args, ignore_case));
        }
    }

    // 4. Translation Path Strings
    auto translate_paths_strings = ParseAndAdvancePointer<PManifestTranslatePathsStrings>(offset);
    for (uint32_t i = 0; i < translate_paths_strings->Count; i++) {
        std::basic_string<PathChar> from;
        std::basic_string<PathChar> to;
        ParseUtf16CharArrayToString(offset, from);
        ParseUtf16CharArrayToString(offset, to);

        if (!to.empty()) {
            translate_paths_.push_back(TranslatePathTuple(from, to));
        }
    }

    // 5. Error Dump Location
    ParseAndAdvancePointer<PManifestInternalDetoursErrorNotificationFileString>(offset);

    // The path is not part of the PManifestInternalDetoursErrorNotificationFileString struct, extract it manually
    // On Linux this does not point to a real path, however to align with the Windows format for the file access manifest this is re-used
    auto error_dump_loc_len = ParseUtf16CharArrayToString(offset, error_dump_location_);

    // 6. Flags
    auto flags = ParseAndAdvancePointer<PCManifestFlags>(offset);
    flags_ = static_cast<FileAccessManifestFlag>(flags->Flags);

    // 7. Extra Flags
    auto extra_flags = ParseAndAdvancePointer<PCManifestExtraFlags>(offset);
    extra_flags_ = static_cast<FileAccessManifestExtraFlag>(extra_flags->ExtraFlags);

    // 8. PipId
    auto pip_id = ParseAndAdvancePointer<PCManifestPipId>(offset);
    pip_id_ = static_cast<uint64_t>(pip_id->PipId);

    // 9. Report
    report_ = ParseAndAdvancePointer<PCManifestReport>(offset);

    // 10. Dll
    dll_ = ParseAndAdvancePointer<PCManifestDllBlock>(offset);

    // 11. Substitute Process Shim Block
    auto shim_info = ParseAndAdvancePointer<PCManifestSubstituteProcessExecutionShim>(offset);
    auto shim_path_len = SkipChar16Array(offset);
    if (shim_path_len > 0) {
        SkipChar16Array(offset); // SubstituteProcessExecutionPluginDll32Path
        SkipChar16Array(offset); // SubstituteProcessExecutionPluginDll64Path

        auto num_process_matches = ParseUint32(offset);
        for (uint32_t i = 0; i < num_process_matches; i++) {
            SkipChar16Array(offset); // ProcessName
            SkipChar16Array(offset); // ArgumentMatch
        }
    }

    // 12. Manifest Tree
    manifest_tree_ = Parse<PCManifestRecord>(offset);
    manifest_tree_->AssertValid();

    // Verify the parsed manifest
    // TODO [pgunasekara]: Change this to run only on Linux when Windows uses this code
    std::string error;
    assert(CheckValidUnixManifestTreeRoot(manifest_tree_, error) == true && error.c_str());

    return true;
}

// Manifest Validation
bool FileAccessManifest::CheckValidUnixManifestTreeRoot(PCManifestRecord node, std::string& error) {
    // empty manifest is ok
    if (node->BucketCount == 0) {
        return true;
    }

    // otherwise, there must be exactly one root node corresponding to the unix root sentinel '/'
    // (see UnixPathRootSentinel from HierarchicalNameTable.cs)
    if (node->BucketCount != 1) {
        error = "Root manifest node is expected to have exactly one child (corresponding to the unix root sentinel: '/')";
        return false;
    }

    unsigned int expectedHash = HashPath(kUnixRootSentinal, 0);
    if (node->GetChildRecord(0)->Hash != expectedHash) {
        error = "Wrong hash code for the unix root sentinel node";
        return false;
    }

    return true;
}

std::basic_string<PathChar> FileAccessManifest::ManifestTreeToString(PCManifestRecord node, const int indent, const int index) {
    if (node == nullptr) {
        node = manifest_tree_;
    }

    PathChar indent_str[indent+1];
    indent_str[indent] = L'\0';
    for (int i = 0; i < indent; i++) indent_str[i] = L' ';
    std::basic_string<PathChar> output("| ");
    output.append(indent_str);
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
        output.append(ManifestTreeToString(child, indent + 2, i));
    }
    return output;
}

// Parsing Functions
template <class T> T FileAccessManifest::Parse(size_t& offset) {
    return reinterpret_cast<T>(&(payload_.get()[offset]));
}

template <class T> T FileAccessManifest::ParseAndAdvancePointer(size_t& offset) {
    T result = Parse<T>(offset);
    result->CheckValid();
    offset += result->GetSize();
    return result;
}

inline uint32_t FileAccessManifest::ParseUint32(size_t& offset) {
    uint32_t i = *(uint32_t*)(&(payload_.get()[offset]));
    offset += sizeof(uint32_t);
    return i;
}

size_t FileAccessManifest::SkipChar16Array(size_t& offset) {
    uint32_t length = ParseUint32(offset);
    // Strings in the BuildXL FAM are encoded in unicode rather than utf-8, so here we explicitly skip 2 bytes per character even on Linux.
    offset += sizeof(char16_t) * length;
    return length;
}

size_t FileAccessManifest::ParseUtf16CharArrayToString(size_t& offset, std::basic_string<PathChar>& output) {
    uint32_t length = ParseUint32(offset);
    if (length == 0) {
        output = "";
        return 0;
    }

    output = std::basic_string<PathChar>();
    output.reserve(length);

    for (int i = 0; i < length; i++) {
        // This is a narrowing cast from char16 to char8.
        // On Unix this is safe, but potentially unsafe on Windows.
        // TODO [pgunasekara]: Update this to avoid casting when running on Windows.
        output.push_back(static_cast<char>(payload_[offset + (i * sizeof(char16_t))]));
    }
    offset += sizeof(char16_t) * length;
    return length;
}

inline BYTE FileAccessManifest::ParseByte(size_t& offset) {
    BYTE b = (BYTE)payload_[offset];
    offset += sizeof(BYTE);
    return b;
}

bool FileAccessManifest::ShouldBreakaway(const PathChar *path, const PathChar *const argv[])
{
    if (breakaway_child_processes_.empty() || path == nullptr)
    {
        return false;
    }

    // Retrieve the image name (last component of the path)
    auto imageName = std::basic_string(basename(path));

    for(auto it = breakaway_child_processes_.begin(); it != breakaway_child_processes_.end(); it++)
    {
        if (imageName.compare(it->GetExecutable()) == 0)
        {
            // If the image name matched and there are no required args, this is a breakaway
            if (it->GetRequiredArgs().empty())
            {
                return true;
            }
            else
            {
                return ContainsRequiredArgs(it->GetRequiredArgs(), it->GetRequiredArgsIgnoreCase(), argv);
            }
        }
    }

    return false;
}

bool FileAccessManifest::ContainsRequiredArgs(const std::basic_string<PathChar>& requiredArgs, bool requiredArgsIgnoreCase, const PathChar *const argv[])
{
    // Argument matching needs to happen against the whole set of arguments, so we need to put the command line back together
    std::basic_string<PathChar> arguments = GetCommandLineFromArgv(argv);

    if (requiredArgsIgnoreCase)
    {
        return FindCaseInsensitively(arguments, requiredArgs) != arguments.end();
    }
    else
    {
        return arguments.find(requiredArgs) != arguments.npos;
    }
}

} // namespace common
} // namespace buildxl