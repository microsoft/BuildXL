// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <cstdio>

#include "ESClient.hpp"
#include "ESConstants.hpp"
#include "IOEvent.hpp"
#include "XPCConstants.hpp"

ESClient::ESClient(dispatch_queue_t event_queue, pid_t host_pid, xpc_endpoint_t endpoint)
{
    assert(endpoint != nullptr);
    host_pid_ = host_pid;
    
    eventQueue_ = event_queue;
    build_host_ = xpc_connection_create_from_endpoint(endpoint);
    xpc_connection_set_event_handler(build_host_, ^(xpc_object_t message)
    {
        xpc_type_t type = xpc_get_type(message);
        if (type == XPC_TYPE_ERROR)
        {
            // NOOP
        }
    });
    
    xpc_connection_resume(build_host_);
    
    es_new_client_result_t result = es_new_client(&client_, ^(es_client_t *c, const es_message_t *message)
    {
        __block es_message_t * observation = es_copy_message(message);
        
        pid_t pid = audit_token_to_pid(observation->process->audit_token);
        if (host_pid_ == pid || getppid() == pid || getpid() == pid)
        {
            es_mute_process(client_, &observation->process->audit_token);
            es_free_message(observation);
            return;
        }
        
        IOEvent event(observation);
        
        size_t msg_length = IOEvent::max_size();
        char msg[msg_length];
        
        omemorystream oms(msg, sizeof(msg));
        oms << event;
        
        xpc_object_t xpc_payload = xpc_dictionary_create(NULL, NULL, 0);
        xpc_dictionary_set_string(xpc_payload, "IOEvent", msg);
        xpc_dictionary_set_uint64(xpc_payload, "IOEvent::Length", event.Size());
        
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
                    if (client_) es_mute_process(client_, &observation->process->audit_token);
                    break;
                case xpc_response_error:
                case xpc_response_failure:
                    break;
            }
            
            es_free_message(observation);
        });
    });

    if (result != ES_NEW_CLIENT_RESULT_SUCCESS)
    {
        exit(1);
    }
    
    es_clear_cache_result_t clear_result = es_clear_cache(client_);
    if (clear_result != ES_CLEAR_CACHE_RESULT_SUCCESS)
    {
        exit(1);
    }
    
    int event_count = sizeof(es_observed_events_) / sizeof(es_observed_events_[0]);
    es_return_t unsubscribe_result = es_subscribe(client_, (es_event_type_t *)es_observed_events_, event_count);
    if (unsubscribe_result != ES_RETURN_SUCCESS)
    {
        exit(1);
    }
    
    log_debug("Successfully initialized the EndpointSecurity sandbox backend, tracking: %d event(s).", event_count);
}

void ESClient::TearDown(xpc_object_t remote, xpc_object_t reply)
{
    if (client_ != nullptr)
    {
        es_return_t result = es_unsubscribe_all(client_);
        if (result != ES_RETURN_SUCCESS)
        {
            log_error("%s", "Failed unsubscribing from all EndpointSecurity events on client tear-down!\n");
        }
        
        result = es_delete_client(client_);
        if (result != ES_RETURN_SUCCESS)
        {
            log_error("%s", "Failed deleting the EndpointSecurity client!\n");
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
}

ESClient::~ESClient()
{
    TearDown();
    log_debug("%s", "Successfully cleaned-up the EndpointSecurity sandbox backend.");
}
