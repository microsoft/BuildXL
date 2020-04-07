// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef DetoursSandbox_hpp
#define DetoursSandbox_hpp

#include "stdafx.h"

#include <errno.h>
#include <unistd.h>
#include <limits.h>
#include <signal.h>
#include <sys/socket.h>
#include <sys/un.h>

#include "IOEvent.hpp"

static const char *socket_path = "/tmp/buildxl_interpose";

class DetoursSandbox final
{

private:
    
    pid_t hostPid_;
    
    int kqueueHandle_ = -1;
    int socketHandle_ = -1;
    
    struct sockaddr_un socketAddr_;
    dispatch_queue_t eventQueue_ = nullptr;
    process_callback eventCallback_ = nullptr;
    
public:
    
    DetoursSandbox() = delete;
    DetoursSandbox(pid_t host_pid, process_callback callback);
    ~DetoursSandbox();
    
    inline const int GetHostPid() const { return hostPid_; }
    inline const int GetSocketHandle() const { return socketHandle_; }
    inline const dispatch_queue_t GetEventQueue() const { return eventQueue_; }
    inline const process_callback GetProcessorCallback() const { return eventCallback_; }
};

#endif /* DetoursSandbox_hpp */
