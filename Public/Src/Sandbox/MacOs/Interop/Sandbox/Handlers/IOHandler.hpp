// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef IOHandler_hpp
#define IOHandler_hpp

#include "AccessHandler.hpp"
#include "IOEvent.hpp"

#define NO_ERROR 0

struct IOHandler final : public AccessHandler
{
public:

    IOHandler(Sandbox *sandbox) : AccessHandler(sandbox) { }

    AccessCheckResult HandleEvent(const IOEvent &event);

#pragma mark Process life cycle
    
    AccessCheckResult HandleProcessFork(const IOEvent &event);

    AccessCheckResult HandleProcessExec(const IOEvent &event);

    AccessCheckResult HandleProcessExit(const IOEvent &event);

    AccessCheckResult HandleProcessUntracked(const pid_t pid);
    
#pragma mark Process I/O observation
    
    AccessCheckResult HandleLookup(const IOEvent &event);
    
    AccessCheckResult HandleOpen(const IOEvent &event);
    
    AccessCheckResult HandleClose(const IOEvent &event);

    AccessCheckResult HandleCreate(const IOEvent &event);
    
    AccessCheckResult HandleLink(const IOEvent &event);
    
    AccessCheckResult HandleUnlink(const IOEvent &event);
    
    AccessCheckResult HandleReadlink(const IOEvent &event);
    
    AccessCheckResult HandleRename(const IOEvent &event);
    
    AccessCheckResult HandleClone(const IOEvent &event);

    AccessCheckResult HandleExchange(const IOEvent &event);
    
    AccessCheckResult HandleGenericWrite(const IOEvent &event);
    
    AccessCheckResult HandleGenericRead(const IOEvent &event);
    
    AccessCheckResult HandleGenericProbe(const IOEvent &event);
};

#endif /* IOHandler_hpp */
