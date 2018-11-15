//
//  TrustedBsdHandler.hpp
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#ifndef TrustedBSDHandler_hpp
#define TrustedBSDHandler_hpp

#include "AccessHandler.hpp"

class TrustedBsdHandler : public AccessHandler
{
public:

    TrustedBsdHandler(DominoSandbox *sandbox)
        : AccessHandler(sandbox) { }

    int HandleLookup(const char *path);

    int HandleReadlink(vnode_t symlinkVNode);
    
    int HandleVNodeCreateEvent(const char *fullPath, const bool isDir, const bool isSymlink);
    
    void HandleProcessFork(const pid_t childProcessPid);
    
    void HandleProcessExit(const pid_t pid);
    
    void HandleProcessUntracked(const pid_t pid);
};

#endif /* TrustedBSDHandler_hpp */
