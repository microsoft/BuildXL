// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef SandboxedProcess_hpp
#define SandboxedProcess_hpp

#include "SandboxedPip.hpp"

/*!
 * Represents a process being tracked.
 *
 * A process always has a pointer to a pip it belongs to.  Additionally, it stores
 * its Process ID as well as the full path to its executable.
 *
 * Process path is updated every time the process performs the 'exec' system call.
 * When a process forks, the child process inherits the path from its parent.
 */
class SandboxedProcess
{
private:

    /*! The pip this process belongs to. */
    SandboxedPip *pip_;

    /*! PID */
    pid_t id_;

    /*! Full path to this process' executable */
    char path_[PATH_MAX];

    /*! The length of the path stored in 'path_' */
    int pathLength_;

public:
    
    SandboxedProcess() = delete;
    SandboxedProcess(pid_t processId, SandboxedPip *pip);
    ~SandboxedProcess();

    /*! The pip this process belongs to */
    SandboxedPip* getPip() const                                { return pip_; }

    /*! Process ID of this process */
    inline pid_t getPid() const                                 { return id_; }

    /*! Returns whether a full path has been set */
    inline bool hasPath() const                                 { return path_[0] == '/'; }

    /*! 0-terminated full path to the executable file of this process */
    inline const char* getPath() const                          { return path_; }

    /*! Copies the 0-terminated string in 'path' to its own path buffer. */
    inline void setPath(const char *path, size_t len = PATH_MAX)   { strlcpy(path_, path, len); }

    /*! An alternative to 'setPath': returns a buffer to which the caller can set the path. */
    inline char* getPathBuffer()                                { return path_; }
};

#endif /* SandboxedProcess_hpp */
