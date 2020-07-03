// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef DetoursSandbox_hpp
#define DetoursSandbox_hpp

#include "stdafx.h"
#include "IOEvent.hpp"

class DetoursSandbox final
{

private:
    
    pid_t hostPid_;
    dispatch_queue_t eventQueue_ = nullptr;
    process_callback eventCallback_ = nullptr;

#if __APPLE__
    xpc_connection_t xpc_bridge_ = nullptr;
    xpc_connection_t detours_ = nullptr;
#endif
    
public:
    
    DetoursSandbox() = delete;
    ~DetoursSandbox();
    
#if __APPLE__
    DetoursSandbox(pid_t host_pid, process_callback callback, void *sandbox, xpc_connection_t bridge);
#endif
};

#endif /* DetoursSandbox_hpp */
