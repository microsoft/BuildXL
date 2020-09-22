// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "ESClient.hpp"
#import "ESConstants.hpp"
#import "XPCConstants.hpp"

// TODO: Get rid of the global state once the prototype is more mature

xpc_endpoint_t detours_endpoint = nullptr;
xpc_endpoint_t es_endpoint = nullptr;

ESClient *lifetime_client = nullptr;
ESClient *exit_client = nullptr;
ESClient *write_client = nullptr;
ESClient *read_client = nullptr;
ESClient *probe_client = nullptr;
ESClient *lookup_client = nullptr;

#define CREATE_HIGH_PRIORITY_QUEUE(name, identifier) \
    dispatch_queue_t name = dispatch_queue_create(identifier, dispatch_queue_attr_make_with_qos_class( \
        DISPATCH_QUEUE_SERIAL, QOS_CLASS_USER_INTERACTIVE, -1 \
    ));

#define INIT(client, queue, events) {\
    int count = sizeof(events) / sizeof(events[0]); \
    client = count > 0 ? new ESClient(queue, (pid_t)host_pid, es_endpoint, (es_event_type_t *)events, count) : nullptr; \
}

#define TEAR_DOWN(client) \
    if (client != nullptr) client->TearDown(peer, reply);

int main(void)
{
    // One consumer queue per event bucket and client

    CREATE_HIGH_PRIORITY_QUEUE(es_lifetime_event_queue, "com.microsoft.buildxl.sandbox.es_liftime_events")
    CREATE_HIGH_PRIORITY_QUEUE(es_exit_event_queue, "com.microsoft.buildxl.sandbox.es_exit_events")
    CREATE_HIGH_PRIORITY_QUEUE(es_write_event_queue, "com.microsoft.buildxl.sandbox.es_write_events")
    CREATE_HIGH_PRIORITY_QUEUE(es_read_event_queue, "com.microsoft.buildxl.sandbox.es_read_events")
    CREATE_HIGH_PRIORITY_QUEUE(es_probe_event_queue, "com.microsoft.buildxl.sandbox.es_probe_events")
    CREATE_HIGH_PRIORITY_QUEUE(es_lookup_event_queue, "com.microsoft.buildxl.sandbox.es_lookup_events")

    dispatch_queue_t xpc_queue = dispatch_queue_create("com.microsoft.buildxl.sandbox.xpc_queue", dispatch_queue_attr_make_with_qos_class(
        DISPATCH_QUEUE_SERIAL, QOS_CLASS_USER_INTERACTIVE, -1
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

                                    INIT(lifetime_client, es_lifetime_event_queue, es_lifetime_events_)
                                    INIT(exit_client, es_exit_event_queue, es_exit_events_)
                                    INIT(write_client, es_write_event_queue, es_write_events_)
                                    INIT(read_client, es_read_event_queue, es_read_events_)
                                    INIT(probe_client, es_probe_event_queue, es_probe_events_)
                                    INIT(lookup_client, es_lookup_event_queue, es_lookup_events_)
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

                                    TEAR_DOWN(lifetime_client)
                                    TEAR_DOWN(exit_client)
                                    TEAR_DOWN(write_client)
                                    TEAR_DOWN(read_client)
                                    TEAR_DOWN(probe_client)
                                    TEAR_DOWN(lookup_client)

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
