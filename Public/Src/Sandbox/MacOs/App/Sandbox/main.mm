// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "ESClient.hpp"
#import "ESConstants.hpp"
#import "XPCConstants.hpp"

xpc_endpoint_t detours_endpoint = nullptr;
xpc_endpoint_t es_endpoint = nullptr;
ESClient *client = nullptr;

int main(void)
{    
    dispatch_queue_t xpc_queue = dispatch_queue_create("com.microsoft.buildxl.sandbox.xpc_queue", dispatch_queue_attr_make_with_qos_class(
        DISPATCH_QUEUE_SERIAL, QOS_CLASS_USER_INITIATED, -1
    ));
    
    dispatch_queue_t es_event_queue = dispatch_queue_create("com.microsoft.buildxl.sandbox.es_eventqueue", dispatch_queue_attr_make_with_qos_class(
        DISPATCH_QUEUE_SERIAL, QOS_CLASS_USER_INITIATED, -1
    ));
    
    xpc_connection_t sandbox = xpc_connection_create_mach_service("com.microsoft.buildxl.sandbox", xpc_queue, XPC_CONNECTION_MACH_SERVICE_LISTENER);
    xpc_connection_set_event_handler(sandbox, ^(xpc_object_t peer)
    {
        xpc_type_t type = xpc_get_type(peer);
        if (type != XPC_TYPE_ERROR)
        {
            xpc_connection_set_event_handler((xpc_connection_t)peer, ^(xpc_object_t message)
            {
                xpc_type_t type = xpc_get_type(message);
                if (type == XPC_TYPE_DICTIONARY)
                {
                    uint64_t command = xpc_dictionary_get_uint64(message, "command");
                    if (command > 0)
                    {
                        switch (command)
                        {
                            case xpc_get_detours_connection:
                            case xpc_get_es_connection:
                            {
                                xpc_object_t reply = xpc_dictionary_create_reply(message);
                                xpc_connection_t connection = (command == xpc_get_detours_connection ? detours_endpoint : es_endpoint);
                                
                                xpc_dictionary_set_value(reply, "connection", connection);
                                xpc_dictionary_set_uint64(reply, "response", connection != nullptr ? xpc_response_success : xpc_response_failure);
                                xpc_connection_send_message(peer, reply);
                                
                                break;
                            }
                            case xpc_set_detours_connection:
                            case xpc_set_es_connection:
                            {
                                if (command == xpc_set_detours_connection)
                                {
                                    detours_endpoint = xpc_dictionary_get_value(message, "connection");
                                }
                                else
                                {
                                    es_endpoint = xpc_dictionary_get_value(message, "connection");
                                    uint64_t host_pid = xpc_dictionary_get_uint64(message, "host_pid");
                                    client = new ESClient(es_event_queue, (pid_t)host_pid, es_endpoint);
                                }
                                
                                xpc_object_t reply = xpc_dictionary_create_reply(message);
                                xpc_dictionary_set_uint64(reply, "response", xpc_response_success);
                                xpc_connection_send_message(peer, reply);
                                
                                break;
                            }
                            case xpc_kill_detours_connection:
                            case xpc_kill_es_connection:
                            {
                                if (command == xpc_kill_detours_connection)
                                {
                                    detours_endpoint = nullptr;
                                }
                                else
                                {
                                    xpc_object_t reply = xpc_dictionary_create_reply(message);
                                    if (client) client->TearDown(peer, reply);
                                    es_endpoint = nullptr;
                                }
                                break;
                            }
                        }
                    }
                }
                else if (type == XPC_TYPE_ERROR)
                {                    
                    // TODO: Get ESClient mappings implemented to do proper cleanup
                }
            });
            
            xpc_connection_resume(peer);
        }
        else
        {
            // NOOP
        }
    });
    
    xpc_connection_activate(sandbox);
    [[NSRunLoop currentRunLoop] run];
}
