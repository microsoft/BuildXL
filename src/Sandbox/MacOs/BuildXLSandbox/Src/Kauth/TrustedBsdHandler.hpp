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

    TrustedBsdHandler(const ProcessObject *process, DominoSandbox *sandbox)
        : AccessHandler(process, sandbox) { }

    int HandleLookup(const char *path);

    int HandleReadlink(vnode_t symlinkVNode);
    
    int HandleVNodeCreateEvent(const char *fullPath, const bool isDir, const bool isSymlink);

private:

    AccessCheckResult CheckCreate(PolicyResult policyResult, bool isDir, bool isSymlink);
};

#endif /* TrustedBSDHandler_hpp */
