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

    // TODO: all these should return AccessCheckResult

    void HandleEvent(const IOEvent &event);

#pragma mark Process life cycle
    
    void HandleProcessFork(const IOEvent &event);

    void HandleProcessExec(const IOEvent &event);

    void HandleProcessExit(const IOEvent &event);

    void HandleProcessUntracked(const pid_t pid);
    
#pragma mark Process I/O observation
    
    void HandleLookup(const IOEvent &event);
    
    void HandleOpen(const IOEvent &event);
    
    void HandleClose(const IOEvent &event);

    void HandleCreate(const IOEvent &event);
    
    void HandleLink(const IOEvent &event);
    
    void HandleUnlink(const IOEvent &event);
    
    void HandleReadlink(const IOEvent &event);
    
    void HandleRename(const IOEvent &event);
    
    void HandleClone(const IOEvent &event);

    void HandleExchange(const IOEvent &event);
    
    void HandleGenericWrite(const IOEvent &event);
    
    void HandleGenericRead(const IOEvent &event);
    
    void HandleGenericProbe(const IOEvent &event);
};

#endif /* IOHandler_hpp */
