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
class SandboxedPip
{

private:

    /*! Process id of the root process of this pip. */
    pid_t processId_;

    /*! File access manifest payload bytes */
    char *payload_;

    /*! File access manifest (contains pointers into the 'payload_' byte array */
    FileAccessManifestParseResult fam_;

    /*! Number of processses in this pip's process tree */
    _Atomic int processTreeCount_;

public:

    SandboxedPip() = delete;
    SandboxedPip(pid_t pid, const char *payload, size_t length);
    ~SandboxedPip();

    /*! Process id of the root process of this pip. */
    pid_t getProcessId() const { return processId_; }

    /*! A unique identifier of this pip. */
    pipid_t getPipId() const   { return fam_.GetPipId()->PipId; }

    /*! File access manifest record for this pip (to be used for checking file accesses) */
    PCManifestRecord getManifestRecord() const    { return fam_.GetUnixRootNode(); }

    /*! File access manifest flags */
    FileAccessManifestFlag getFamFlags() const    { return fam_.GetFamFlags(); }

    /*!
     * Returns the full path of the root process of this pip.
     * The lenght of the path is stored in the 'length' argument because the path is not necessarily 0-terminated.
     */
    const char* getProcessPath(int *length) const { return fam_.GetProcessPath(length); }

#pragma mark Process Tree Tracking

    /*! Number of currently active processes in this pip's process tree */
    int getTreeSize() const          { return processTreeCount_; }

    /*! Atomically increments this pip's process tree size and returns the size before increment. */
    int incrementProcessTreeCount() { return ++processTreeCount_; }

    /*! Atomically dencrements this pip's process tree size and returns the size before decrement. */
    int decrementProcessTreeCount() { return --processTreeCount_; }
};

#endif /* SandboxedPip_hpp */
