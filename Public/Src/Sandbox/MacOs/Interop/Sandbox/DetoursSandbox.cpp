// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "BuildXLSandboxShared.hpp"
#include "BuildXLException.hpp"
#include "DetoursSandbox.hpp"
#include "XPCConstants.hpp"

DetoursSandbox::DetoursSandbox(pid_t host_pid, process_callback callback, void *sandbox, xpc_connection_t bridge)
{
    assert(callback != nullptr && bridge != nullptr);

    eventCallback_ = callback;
    xpc_bridge_ = bridge;
    hostPid_ = host_pid;

    char queueName[PATH_MAX] = { '\0' };
    sprintf(queueName, "com.microsoft.buildxl.detours.eventqueue_%d", host_pid);

    eventQueue_ = dispatch_queue_create(queueName, dispatch_queue_attr_make_with_qos_class(
        DISPATCH_QUEUE_SERIAL, QOS_CLASS_USER_INTERACTIVE, -1
    ));

    detours_ = xpc_connection_create(NULL, NULL);
    xpc_connection_set_event_handler(detours_, ^(xpc_object_t peer)
    {
        xpc_type_t type = xpc_get_type(peer);
        if (type != XPC_TYPE_ERROR)
        {
            xpc_connection_set_event_handler((xpc_connection_t) peer, ^(xpc_object_t message)
            {
                xpc_type_t type = xpc_get_type(message);
                if (type == XPC_TYPE_DICTIONARY)
                {
                    const char *msg = xpc_dictionary_get_string(message, IOEventKey);
                    const uint64_t msg_length = xpc_dictionary_get_uint64(message, IOEventLengthKey);

                    imemorystream ims(msg, msg_length);
                    ims.imbue(std::locale(ims.getloc(), new PipeDelimiter));
                    IOEvent event;
                    ims >> event;

                    eventCallback_(sandbox, const_cast<const IOEvent &>(event), hostPid_, IOEventBacking::Interposing);

                    xpc_object_t reply = xpc_dictionary_create_reply(message);
                    xpc_dictionary_set_uint64(reply, "response", xpc_response_success);
                    xpc_connection_send_message((xpc_connection_t) peer, reply);
                }
                else if (type == XPC_TYPE_ERROR)
                {
                    if (message == XPC_ERROR_CONNECTION_INTERRUPTED)
                    {

                    }
                    else if (message == XPC_ERROR_CONNECTION_INVALID)
                    {

                    }
                }
            });
            xpc_connection_resume((xpc_connection_t)peer);
        }
        else
        {
            // NOOP
        }
    });

    xpc_connection_set_target_queue(detours_, eventQueue_);
    xpc_connection_resume(detours_);

    xpc_object_t post = xpc_dictionary_create(NULL, NULL, 0);
    xpc_dictionary_set_uint64(post, "command", xpc_set_detours_connection);
    xpc_dictionary_set_connection(post, "connection", detours_);

    xpc_object_t response = xpc_connection_send_message_with_reply_sync(xpc_bridge_, post);
    xpc_type_t type = xpc_get_type(response);

    uint64_t status = 0;

    if (type == XPC_TYPE_DICTIONARY)
    {
        status = xpc_dictionary_get_uint64(response, "response");
        log_debug("Successfully initialized the Detours sandbox backend - status(%lld).", status);
    }

    xpc_release(response);


    if (status != xpc_response_success)
    {
        throw BuildXLException("Could not connect to sandbox XPC bridge, aborting!");
    }
}

DetoursSandbox::~DetoursSandbox()
{
    xpc_object_t post = xpc_dictionary_create(NULL, NULL, 0);
    xpc_dictionary_set_uint64(post, "command", xpc_kill_detours_connection);
    xpc_connection_send_message(xpc_bridge_, post);

    xpc_connection_cancel(detours_);
    xpc_release(detours_);

    xpc_bridge_ = nullptr;
    detours_ = nullptr;

    if (eventQueue_ != nullptr)
    {
        dispatch_release(eventQueue_);
    }

    eventCallback_ = nullptr;
}
