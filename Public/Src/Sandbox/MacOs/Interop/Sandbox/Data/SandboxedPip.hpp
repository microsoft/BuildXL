// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef SandboxedPip_hpp
#define SandboxedPip_hpp

#include "BuildXLSandboxShared.hpp"
#include "FileAccessManifestParser.hpp"

/*!
 * Represents the root of the process tree being tracked.
 *
 * The 'Pip' name comes from the BuildXL terminology, where 'pip' is a generic build task
 * that may spawn arbitrary number of child processes.
 *
 * Every pip comes with a 'FileAccessManifest' (FAM).  A FAM contains all the policies relevant
 * for sandboxing a pip, e.g., which file accesses are permitted, which are not, which should
 * be reported back, etc.
 */

class SandboxedPip final
{

private:
    
    /*! Process id of the root process of this pip. */
    pid_t processId_;

    /*! File access manifest payload bytes */
    char *payload_;

    /*! File access manifest (contains pointers into the 'payload_' byte array */
    FileAccessManifestParseResult fam_;

    /*! Number of processses in this pip's process tree */
    std::atomic<int> processTreeCount_;
    
public:

    SandboxedPip() = delete;
    SandboxedPip(pid_t pid, const char *payload, size_t length);
    ~SandboxedPip();

    /*! Process id of the root process of this pip. */
    inline const pid_t GetProcessId() const                 { return processId_; }

    /*! A unique identifier of this pip. */
    inline const pipid_t GetPipId() const                   { return fam_.GetPipId()->PipId; }

    /*! File access manifest record for this pip (to be used for checking file accesses) */
    inline const PCManifestRecord GetManifestRecord() const { return fam_.GetUnixRootNode(); }

    /*! File access manifest flags */
    inline const FileAccessManifestFlag GetFamFlags() const { return fam_.GetFamFlags(); }

    /*!
     * Returns the full path of the root process of this pip.
     * The lenght of the path is stored in the 'length' argument because the path is not necessarily 0-terminated.
     */
    inline const char* GetProcessPath(int *length) const    { return fam_.GetProcessPath(length); }

    /*! Number of currently active processes in this pip's process tree */
    inline const int GetTreeSize() const                    { return processTreeCount_; }
    
    /*! When this returns true, child processes should not be tracked. */
    bool AllowChildProcessesToBreakAway() const             { return fam_.AllowChildProcessesToBreakAway(); }
    

#pragma mark Process Tree Tracking

    /*! Atomically increments this pip's process tree size and returns the size before increment. */
    inline const int IncrementProcessTreeCount() { return ++processTreeCount_; }

    /*! Atomically dencrements this pip's process tree size and returns the size before decrement. */
    inline const int DecrementProcessTreeCount() { return --processTreeCount_; }
};

#endif /* SandboxedPip_hpp */
