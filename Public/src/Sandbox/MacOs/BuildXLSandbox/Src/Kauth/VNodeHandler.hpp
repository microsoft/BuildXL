//
//  VNodeHandler.hpp
//  VNodeHandler
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#ifndef VNodeHandler_hpp
#define VNodeHandler_hpp

#include "AccessHandler.hpp"
#include "PolicyResult.h"

#define VNODE_CREATE 0

bool ConstructVNodeActionString(kauth_action_t action,
                                bool isDir,
                                const char *separator,
                                char *result,
                                int *resultLength);

class VNodeHandler : public AccessHandler
{
    private:

        AccessCheckResult CheckExecute(PolicyResult policyResult, bool isDir);

        AccessCheckResult CheckProbe(PolicyResult policyResult, bool isDir);

        AccessCheckResult CheckRead(PolicyResult policyResult, bool isDir);

        AccessCheckResult CheckWrite(PolicyResult policyResult, bool isDir);

    public:

        VNodeHandler(const ProcessObject *process, DominoSandbox *sandbox)
            : AccessHandler(process, sandbox) { }

        int HandleVNodeEvent(const kauth_cred_t credential,
                             const void *idata,
                             const kauth_action_t action,
                             const vfs_context_t context,
                             const vnode_t vp,
                             const vnode_t dvp,
                             const uintptr_t arg3);

        static bool CreateVnodePath(vnode_t vp, char *result, int len);
};

#endif /* VNodeHandler_hpp */
