// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef EndpointSecuritySandbox_hpp
#define EndpointSecuritySandbox_hpp

#include <bsm/libbsm.h>
#include <dispatch/dispatch.h>
#include <EndpointSecurity/EndpointSecurity.h>

#include "IOEvent.hpp"

// Currently the following events are not hooked up, maybe useful later
// ES_EVENT_TYPE_NOTIFY_READDIR,
// ES_EVENT_TYPE_NOTIFY_FSGETPATH,
// ES_EVENT_TYPE_NOTIFY_DUP,
// ES_EVENT_TYPE_NOTIFY_WRITE, // Slows down ES due to callback being invoked on every write

const es_event_type_t es_observed_events_[] =
{
    // Process life cycle
    ES_EVENT_TYPE_NOTIFY_EXEC,
    ES_EVENT_TYPE_NOTIFY_FORK,
    ES_EVENT_TYPE_NOTIFY_EXIT,

    ES_EVENT_TYPE_NOTIFY_OPEN,
    ES_EVENT_TYPE_NOTIFY_CLOSE,

    // Read events
    ES_EVENT_TYPE_NOTIFY_READLINK,
    ES_EVENT_TYPE_NOTIFY_GETATTRLIST,
    ES_EVENT_TYPE_NOTIFY_GETEXTATTR,
    ES_EVENT_TYPE_NOTIFY_LISTEXTATTR,
    ES_EVENT_TYPE_NOTIFY_ACCESS,
    ES_EVENT_TYPE_NOTIFY_STAT,

    // Write events
    ES_EVENT_TYPE_NOTIFY_CREATE,
    ES_EVENT_TYPE_NOTIFY_TRUNCATE,
    ES_EVENT_TYPE_NOTIFY_CLONE,
    ES_EVENT_TYPE_NOTIFY_EXCHANGEDATA,
    ES_EVENT_TYPE_NOTIFY_RENAME,

    ES_EVENT_TYPE_NOTIFY_LINK,
    ES_EVENT_TYPE_NOTIFY_UNLINK,

    ES_EVENT_TYPE_NOTIFY_SETATTRLIST,
    ES_EVENT_TYPE_NOTIFY_SETEXTATTR,
    ES_EVENT_TYPE_NOTIFY_DELETEEXTATTR,
    ES_EVENT_TYPE_NOTIFY_SETFLAGS,
    ES_EVENT_TYPE_NOTIFY_SETMODE,
    ES_EVENT_TYPE_NOTIFY_SETOWNER,
    ES_EVENT_TYPE_NOTIFY_SETACL,

    ES_EVENT_TYPE_NOTIFY_LOOKUP
};

class EndpointSecuritySandbox final
{

private:
    
    es_client_t *client_ = nullptr;
    dispatch_queue_t eventQueue_ = nullptr;
    process_callback cb_ = nullptr;
    
public:
    
    EndpointSecuritySandbox() = delete;
    EndpointSecuritySandbox(pid_t host_pid, process_callback callback);
    ~EndpointSecuritySandbox();
};

#endif /* EndpointSecuritySandbox_hpp */
