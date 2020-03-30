// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "BuildXLSandboxShared.hpp"
#include "BuildXLException.hpp"
#include "EndpointSecuritySandbox.hpp"

EndpointSecuritySandbox::EndpointSecuritySandbox(pid_t host_pid, process_callback callback)
{
    assert(callback != nullptr);
    cb_ = callback;
    
    char queueName[PATH_MAX] = { '\0' };
    sprintf(queueName, "com.microsoft.buildxl.es.eventqueue_%d", host_pid);
    
    eventQueue_ = dispatch_queue_create(queueName, dispatch_queue_attr_make_with_qos_class(
        DISPATCH_QUEUE_SERIAL, QOS_CLASS_USER_INITIATED, -1
    ));
    
    es_new_client_result_t result = es_new_client(&client_, ^(es_client_t *c, const es_message_t *msg)
    {
        __block es_message_t *cpy = es_copy_message(msg);
        dispatch_async(eventQueue_, ^()
        {
            IOEvent event(cpy);
            cb_(c, event, host_pid, IOEventBacking::EndpointSecurity);
            es_free_message(cpy);
        });
    });

    if (result != ES_NEW_CLIENT_RESULT_SUCCESS)
    {
        throw BuildXLException("Failed creating EndpointSecurity client with error code: " + std::to_string(result));
    }
    
    es_clear_cache_result_t clear_result = es_clear_cache(client_);
    if (clear_result != ES_CLEAR_CACHE_RESULT_SUCCESS)
    {
        throw BuildXLException("Failed resetting result cache on EndpointSecurity client initialization!");
    }
    
    int event_count = sizeof(es_observed_events_) / sizeof(es_observed_events_[0]);
    es_return_t status = es_subscribe(client_, (es_event_type_t *)es_observed_events_, event_count);
    
    if (status != ES_RETURN_SUCCESS)
    {
        throw BuildXLException("Failed subscribing to EndpointSecurity events, please check the sandbox configuration!");
    }
    
    log_debug("Successfully initialized the EndpointSecurity sandbox backend, tracking: %d event(s).", event_count);
}

EndpointSecuritySandbox::~EndpointSecuritySandbox()
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
    }
    
    if (eventQueue_ != nullptr)
    {
        dispatch_release(eventQueue_);
    }
    
    client_ = nullptr;
    cb_ = nullptr;
}
