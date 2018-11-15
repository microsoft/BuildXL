//
//  VNodeHandler.hpp
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#ifndef VNodeHandler_hpp
#define VNodeHandler_hpp

#include "AccessHandler.hpp"
#include "OpNames.hpp"
#include "PolicyResult.h"

#define VNODE_CREATE 0

typedef struct _FlagsToCheckFunc
{
    int flags;
    FileOperation operation;
    CheckFunc checker;
} FlagsToCheckFunc;

bool ConstructVNodeActionString(kauth_action_t action,
                                bool isDir,
                                const char *separator,
                                char *result,
                                int *resultLength);

class VNodeHandler : public AccessHandler
{
public:

    VNodeHandler(DominoSandbox *sandbox) : AccessHandler(sandbox) { }

    int HandleVNodeEvent(const kauth_cred_t credential,
                         const void *idata,
                         const kauth_action_t action,
                         const vfs_context_t context,
                         const vnode_t vp,
                         const vnode_t dvp,
                         const uintptr_t arg3);
};

#endif /* VNodeHandler_hpp */
