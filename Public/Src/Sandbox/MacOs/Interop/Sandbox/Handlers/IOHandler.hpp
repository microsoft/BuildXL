// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef IOHandler_hpp
#define IOHandler_hpp

#ifdef ES_SANDBOX

#include "AccessHandler.hpp"

struct IOHandler : public AccessHandler
{
public:

    IOHandler(ESSandbox *sandbox) : AccessHandler(sandbox) { }

#pragma mark Process life cycle
    
    void HandleProcessFork(const es_message_t *msg);

    void HandleProcessExec(const es_message_t *msg);

    void HandleProcessExit(const es_message_t *msg);

    void HandleProcessUntracked(const pid_t pid);
    
#pragma mark Process I/O observation
    
    void HandleLookup(const es_message_t *msg);
    
    void HandleOpen(const es_message_t *msg);
    
    void HandleClose(const es_message_t *msg);
    
    void HandleLink(const es_message_t *msg);
    
    void HandleUnlink(const es_message_t *msg);
    
    void HandleReadlink(const es_message_t *msg);
    
    void HandleRename(const es_message_t *msg);
    
    void HandleExchange(const es_message_t *msg);
    
    void HandleCreate(const es_message_t *msg);
    
    void HandleWrite(const es_message_t *msg);
};

#endif /* ES_SANDBOX */

#endif /* IOHandler_hpp */
