// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef FileAccessManifestParser_hpp
#define FileAccessManifestParser_hpp

#include "FileAccessHelpers.h"

struct FileAccessManifestParseResult
{

private:

    PCManifestDebugFlag debugFlag_;
    PCManifestInjectionTimeout injectionTimeoutFlag_;
    PManifestTranslatePathsStrings manifestTranslatePathsStrings_;
    PCManifestFlags flags_;
    PCManifestExtraFlags extraFlags_;
    PCManifestPipId pipId_;
    PCManifestReport report_;
    PCManifestDllBlock dllBlock_;
    PCManifestSubstituteProcessExecutionShim shim_;
    PCManifestRecord root_;
    const char *error_;

    template <class T> T Parse(const BYTE *&payload)
    {
        return reinterpret_cast<T>(payload);
    }

    template <class T> T ParseAndAdvancePointer(const BYTE *&payload)
    {
        T result = Parse<T>(payload);
        this->error_ = result->CheckValid();
        payload += result->GetSize();
        
        return result;
    }

public:

    FileAccessManifestParseResult() {}

    bool init(const BYTE *payload, size_t payloadSize);

    inline bool IsValid() const                         { return error_ == nullptr; }
    inline bool HasErrors() const                       { return !IsValid(); }
    inline const char* Error() const                    { return error_; }
    inline PCManifestRecord GetManifestRootNode() const { return root_; }
    inline PCManifestRecord GetUnixRootNode() const     { return root_->BucketCount > 0 ? root_->GetChildRecord(0) : root_; }
    inline PCManifestPipId GetPipId() const             { return pipId_; }
    inline FileAccessManifestFlag GetFamFlags() const   { return static_cast<FileAccessManifestFlag>(flags_->Flags); }
    inline const char* GetProcessPath(int *length) const
    {
        *length = report_->Size;
        return report_->Report.ReportPath;
    }

    // Debugging helper
    static void PrintManifestTree(PCManifestRecord node, const int indent = 0, const int index = 0);
};

#endif /* FileAccessManifestParser_hpp */
