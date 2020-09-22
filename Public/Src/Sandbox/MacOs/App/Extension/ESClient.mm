// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <cstdio>

#include "ESClient.hpp"
#include "ESConstants.hpp"
#include "IOEvent.hpp"
#include "XPCConstants.hpp"

ESClient::ESClient(dispatch_queue_t event_queue, pid_t host_pid, xpc_endpoint_t endpoint, es_event_type_t *events, uint32_t event_count)
{
    assert(event_queue != nullptr);
    assert(endpoint != nullptr);
    assert(events != nullptr && event_count > 0);

    host_pid_ = host_pid;
    eventQueue_ = event_queue;
    build_host_ = xpc_connection_create_from_endpoint(endpoint);

    xpc_connection_set_event_handler(build_host_, ^(xpc_object_t message)
    {
        xpc_type_t type = xpc_get_type(message);
        if (type == XPC_TYPE_ERROR)
        {
            // TODO: Implement proper protocol for XPC connection handling
        }
    });

    xpc_connection_resume(build_host_);

    es_new_client_result_t result = es_new_client(&client_, ^(es_client_t *c, const es_message_t *message)
    {
        pid_t pid = audit_token_to_pid(message->process->audit_token);
        if (host_pid_ == pid || getppid() == pid || getpid() == pid)
        {
            if (message->action_type == ES_ACTION_TYPE_AUTH)
            {
                switch(message->event_type)
                {
                    case ES_EVENT_TYPE_AUTH_OPEN:
                    {
                        // Currently the ES client allows every flag based event without exception
                        es_respond_flags_result(client_,message, 0x7fffffff, false);
                        break;
                    }
                    default:
                    {
                        // Currently the ES client allows every auth based event without exception
                        es_respond_auth_result(client_, message, ES_AUTH_RESULT_ALLOW, false);
                        break;
                    }
                }
            }

            es_mute_process(client_, &message->process->audit_token);
            return;
        }

        IOEvent event(message);
        size_t msg_length = IOEvent::max_size();
        char msg[msg_length];

        omemorystream oms(msg, sizeof(msg));
        oms << event;

        xpc_object_t xpc_payload = xpc_dictionary_create(NULL, NULL, 0);
        xpc_dictionary_set_string(xpc_payload, IOEventKey, msg);
        xpc_dictionary_set_uint64(xpc_payload, IOEventLengthKey, event.Size());

        xpc_connection_send_message_with_reply(build_host_, xpc_payload, eventQueue_, ^(xpc_object_t response)
        {
            uint64_t status = 0;
            xpc_type_t xpc_type = xpc_get_type(response);
            if (xpc_type == XPC_TYPE_DICTIONARY)
            {
                status = xpc_dictionary_get_uint64(response, "response");
            }

            switch (status)
            {
                case xpc_response_mute_process:
                    if (client_) es_mute_process(client_, event.GetProcessAuditToken());
                    break;
                case xpc_response_auth:
                {
                    if (client_)
                    {
                        switch(message->event_type)
                        {
                            case ES_EVENT_TYPE_AUTH_OPEN:
                                es_respond_flags_result(client_,message, 0x7fffffff, false);
                                break;
                            default:
                                es_respond_auth_result(client_, message, ES_AUTH_RESULT_ALLOW, false);
                                break;
                        }
                    }
                    break;
                }
                case xpc_response_error:
                case xpc_response_failure:
                {
                    log_error("%s", "XPC event processing error - sandboxing is no longer reliable!\n");
                    exit(EXIT_FAILURE);
                    break;
                }
            }
        });
    });

    /*
        TODO: Calling exit() within the system extension causes it to be killed and restarted, in general we have to
              implement a nicer way to indicate errors and recover from them.
     */

    if (result != ES_NEW_CLIENT_RESULT_SUCCESS)
    {
        exit(EXIT_FAILURE);
    }

    es_clear_cache_result_t clear_result = es_clear_cache(client_);
    if (clear_result != ES_CLEAR_CACHE_RESULT_SUCCESS)
    {
        exit(EXIT_FAILURE);
    }

    es_return_t subscribe_result = es_subscribe(client_, (es_event_type_t *)events, event_count);
    if (subscribe_result != ES_RETURN_SUCCESS)
    {
        exit(EXIT_FAILURE);
    }

    log_debug("Successfully initialized an EndpointSecurity client, tracking: %d event(s).", event_count);
}

int ESClient::TearDown(xpc_object_t remote, xpc_object_t reply)
{
    if (client_ != nullptr)
    {
        es_return_t result = es_unsubscribe_all(client_);
        if (result != ES_RETURN_SUCCESS)
        {
            log_error("%s", "Failed unsubscribing from all EndpointSecurity events on client tear-down!\n");
            return ES_RETURN_ERROR;
        }

        result = es_delete_client(client_);
        if (result != ES_RETURN_SUCCESS)
        {
            log_error("%s", "Failed deleting the EndpointSecurity client!\n");
            return ES_RETURN_ERROR;
        }

        client_ = nullptr;

        if (remote && reply)
        {
            dispatch_async(eventQueue_, ^()
            {
                xpc_dictionary_set_uint64(reply, "response", xpc_response_success);
                xpc_connection_send_message(remote, reply);
            });
        }

        eventQueue_ = nullptr;
    }

    return ES_RETURN_SUCCESS;
}

ESClient::~ESClient()
{
    if (ES_RETURN_SUCCESS == TearDown())
    {
        log_debug("%s", "Successfully cleaned-up EndpointSecurity client.");
    }
}
