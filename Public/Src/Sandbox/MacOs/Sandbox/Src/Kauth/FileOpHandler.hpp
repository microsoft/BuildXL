// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef FileOpHandler_hpp
#define FileOpHandler_hpp

#include "AccessHandler.hpp"

class FileOpHandler : public AccessHandler
{
public:

    FileOpHandler(BuildXLSandbox *sandbox) :
        AccessHandler(sandbox) { }

    int HandleFileOpEvent(const kauth_cred_t credential,
                          const void *idata,
                          const kauth_action_t action,
                          const uintptr_t arg0,
                          const uintptr_t arg1,
                          const uintptr_t arg2,
                          const uintptr_t arg3);
};

#endif /* FileOpHandler_hpp */
