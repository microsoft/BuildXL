// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef TrustedBSDHandler_hpp
#define TrustedBSDHandler_hpp

#include "AccessHandler.hpp"

class TrustedBsdHandler : public AccessHandler
{
public:

    TrustedBsdHandler(BuildXLSandbox *sandbox)
        : AccessHandler(sandbox) { }

    int HandleLookup(const char *path);

    int HandleReadVnode(vnode_t vnode, FileOperation operationToReport, bool isVnodeDir);

    int HandleVNodeCreateEvent(const char *fullPath, const bool isDir, const bool isSymlink);

    int HandleVnodeWrite(vnode_t vnode, FileOperation operation);

    int HandleWritePath(const char *path, FileOperation operation);

    void HandleProcessWantsToFork(const pid_t parentProcessPid);

    void HandleProcessFork(const pid_t childProcessPid);

    void HandleProcessExec(const vnode_t vp);

    void HandleProcessExit(const pid_t pid);

    void HandleProcessUntracked(const pid_t pid);
};

#endif /* TrustedBSDHandler_hpp */
