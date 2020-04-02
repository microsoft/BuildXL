// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef SandboxedProcess_hpp
#define SandboxedProcess_hpp

#include "SandboxedPip.hpp"

/*!
 * Represents a process being tracked.
 *
 * A process always has a shared pointer to a pip it belongs to.  Additionally, it stores
 * its Process ID as well as the full path to its executable.
 *
 * Process path is updated every time the process performs the 'exec' system call.
 * When a process forks, the child process inherits the path from its parent.
 */

class SandboxedProcess final
{

private:
    
    /*! The pip this process belongs to. */
    std::shared_ptr<SandboxedPip> pip_;

    /*! PID */
    pid_t id_;

    /*! Full path to this process' executable */
    char path_[PATH_MAX];

    /*! The length of the path stored in 'path_' */
    int pathLength_;

public:
    
    SandboxedProcess() = delete;
    SandboxedProcess(pid_t processId, std::shared_ptr<SandboxedPip> pip);
    ~SandboxedProcess();

    /*! The pip this process belongs to */
    inline const std::shared_ptr<SandboxedPip> GetPip() const    { return pip_; }

    /*! Process ID of this process */
    inline const pid_t GetPid() const                            { return id_; }

    /*! Returns whether a full absolute path has been set */
    inline bool HasPath() const                                  { return path_[0] == '/'; }

    /*! 0-terminated full path to the executable file of this process */
    inline const char* GetPath() const                           { return path_; }

    /*! Copies the 0-terminated string in 'path' to its own path buffer. */
    inline void SetPath(const char *path) { strncpy(path_, path, PATH_MAX); path_[PATH_MAX-1] = '\0'; }
};

#endif /* SandboxedProcess_hpp */
