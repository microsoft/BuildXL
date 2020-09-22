// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef ESClient_hpp
#define ESClient_hpp

#include <EndpointSecurity/EndpointSecurity.h>
#include <Foundation/Foundation.h>

class ESClient final
{

private:

    pid_t host_pid_;

    es_client_t *client_ = nullptr;
    dispatch_queue_t eventQueue_ = nullptr;
    xpc_connection_t build_host_ = nullptr;

public:

    ESClient(dispatch_queue_t event_queue, pid_t host_pid, xpc_endpoint_t endpoint, es_event_type_t *events, uint32_t event_count);
    ~ESClient();

    int TearDown(xpc_object_t remote = nullptr, xpc_object_t reply = nullptr);
};


#endif /* ESClient_hpp */
