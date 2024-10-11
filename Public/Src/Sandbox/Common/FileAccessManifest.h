// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef BUILDXL_SANDBOX_COMMON_FILE_ACCESS_MANIFEST_H
#define BUILDXL_SANDBOX_COMMON_FILE_ACCESS_MANIFEST_H

#include <vector>
#include <string>
#include "DataTypes.h"
#include "StringOperations.h"

namespace buildxl {
namespace common {

// TODO [pgunasekara]: Remove the matching definition in Public/Src/Sandbox/Windows/DetoursServices/DetoursServices.h
/**
 * Breakaway child process information.
 */
typedef struct BreakawayChildProcess {
private:
    std::basic_string<PathChar> executable;
    std::basic_string<PathChar> required_args;
    bool ignore_case;

public:
    BreakawayChildProcess(std::basic_string<PathChar> executable, std::basic_string<PathChar> required_args, bool ignore_case) {
        this->executable = executable;
        this->required_args = required_args;
        this->ignore_case = ignore_case;
    }

    BreakawayChildProcess(const BreakawayChildProcess &other)
        : BreakawayChildProcess(other.executable, other.required_args, other.ignore_case)
    {}

    // The executable name (image name) of the process to breakaway
    std::basic_string<PathChar> const & GetExecutable() {
        return executable;
    }

    // If non-empty, a substring of the arguments passed to the process to breakaway
    std::basic_string<PathChar> const & GetRequiredArgs() {
        return required_args;
    }

    // Whether the required arguments are to be matched ignoring case
    bool GetRequiredArgsIgnoreCase() {
        return ignore_case;
    }
} BreakawayChildProcess;

// TODO: Remove the matching definition in Public/Src/Sandbox/Windows/DetoursServices/DetoursServices.h
/**
 * Tuple of paths to translate.
 */
typedef struct TranslatePathTuple {
private:
    std::basic_string<PathChar> from_path;
    std::basic_string<PathChar> to_path;

public:
    TranslatePathTuple(std::basic_string<PathChar> from, std::basic_string<PathChar> to) {
        from_path = from;
        to_path = to;
    }

    std::basic_string<PathChar> const & GetToPath() {
        return to_path;
    }

    std::basic_string<PathChar> const & GetFromPath() {
        return from_path;
    }
} TranslatePathTuple;

/**
 * Parses the file access manifest payload and stores the information in a FileAccessManifest object.
 */
class FileAccessManifest {
private:
    // CODESYNC: Public/Src/Utilities/Utilities.Core/HierarchicalNameTable.cs
    const PathChar* kUnixRootSentinal = "";

    std::unique_ptr<BYTE []> payload_;
    size_t payload_size_;

    unsigned long injection_timeout_minutes_;
    std::vector<BreakawayChildProcess> breakaway_child_processes_;
    std::vector<TranslatePathTuple> translate_paths_;
    std::basic_string<PathChar> error_dump_location_;
    FileAccessManifestFlag flags_;
    FileAccessManifestExtraFlag extra_flags_;
    uint64_t pip_id_;
    PCManifestReport report_;
    PCManifestDllBlock dll_;
    PCManifestSubstituteProcessExecutionShim shim_info_;
    std::basic_string<PathChar> shim_path_;
    PCManifestRecord manifest_tree_;

    /**
     * Parses the serialized manifest payload from the provided payload.
     */
    bool ParseFileAccessManifest();
    template <class T> T Parse(size_t& offset);
    template <class T> T ParseAndAdvancePointer(size_t& offset);
    uint32_t ParseUint32(size_t& offset);
    size_t SkipChar16Array(size_t& offset);
    size_t ParseUtf16CharArrayToString(size_t& offset, std::basic_string<PathChar>& output);
    BYTE ParseByte(size_t& offset);
    bool CheckValidUnixManifestTreeRoot(PCManifestRecord node, std::string& error);
    bool ContainsRequiredArgs(const std::basic_string<PathChar>& requiredArgs, bool requiredArgsIgnoreCase, const PathChar *const argv[]);
public:
    /**
     * Construct a file access manifest object.
     * This constructor will create a copy of the payload.
     * @param payload The serialized manifest payload.
     * @param payload_size The size of the payload.
     */
    FileAccessManifest(char *payload, size_t payload_size);
    ~FileAccessManifest();

    inline FileAccessManifestFlag GetFlags() const                          { return flags_; }
    inline FileAccessManifestExtraFlag GetExtraFlags() const                { return extra_flags_; }
    inline const char* GetInternalErrorDumpLocation() const                 { return error_dump_location_.c_str(); }
    inline uint64_t GetPipId() const                                        { return pip_id_; }
    inline PCManifestReport GetReport() const                               { return report_; }
    inline PCManifestDllBlock GetDll() const                                { return dll_; }
    inline PCManifestSubstituteProcessExecutionShim GetShimInfo() const     { return shim_info_; }
    inline PCManifestRecord GetManifestTreeRoot() const                     { return manifest_tree_; }
    inline PCManifestRecord GetUnixManifestTreeRoot() const                 { return manifest_tree_->BucketCount > 0 ? manifest_tree_->GetChildRecord(0) : manifest_tree_; }
    // TODO [pgunasekara]: accept a length argument as reference instead of a pointer.
    inline const char *GetReportsPath(int *length) const                    { *length = report_->Size; return report_->Report.ReportPath; }
    bool ShouldBreakaway(const PathChar *path, const PathChar *const argv[]);
    
    // Debugging Helpers
    std::basic_string<PathChar> ManifestTreeToString(PCManifestRecord node = nullptr, const int indent = 0, const int index = 0);
};

} // namespace common
} // namespace buildxl

#endif // BUILDXL_SANDBOX_COMMON_FILE_ACCESS_MANIFEST_H