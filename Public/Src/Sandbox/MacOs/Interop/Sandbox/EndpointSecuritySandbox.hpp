// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef EndpointSecuritySandbox_hpp
#define EndpointSecuritySandbox_hpp

#include "IOEvent.hpp"

class EndpointSecuritySandbox final
{

private:
    
    pid_t hostPid_;
    dispatch_queue_t eventQueue_ = nullptr;
    process_callback eventCallback_ = nullptr;
    
#if __APPLE__
    xpc_connection_t xpc_bridge_ = nullptr;
    xpc_connection_t es_connection_ = nullptr;
#endif
    
public:
    
    EndpointSecuritySandbox() = delete;
    ~EndpointSecuritySandbox();
    
#if __APPLE__
    EndpointSecuritySandbox(pid_t host_pid, process_callback callback, void *sandbox, xpc_connection_t bridge);
#endif
};

#endif /* EndpointSecuritySandbox_hpp */
