// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef ESConstants_h
#define ESConstants_h

#include <bsm/libbsm.h>
#include <dispatch/dispatch.h>
#include <EndpointSecurity/EndpointSecurity.h>
#include <Foundation/Foundation.h>
#include <os/log.h>

#include "IOEvent.hpp"

/*
 
 Currently the following events are not hooked up, maybe useful for later:
 
    ES_EVENT_TYPE_NOTIFY_READDIR,
    ES_EVENT_TYPE_NOTIFY_FSGETPATH,
    ES_EVENT_TYPE_NOTIFY_DUP,
    ES_EVENT_TYPE_NOTIFY_WRITE, // Slows down ES due to callback being invoked on every write
 
 The following are disabled because ES drops events when using all the events we're interested in:
 
    ES_EVENT_TYPE_NOTIFY_OPEN,
    
    ES_EVENT_TYPE_NOTIFY_STAT,
    ES_EVENT_TYPE_NOTIFY_ACCESS,
    ES_EVENT_TYPE_NOTIFY_READLINK,
    ES_EVENT_TYPE_NOTIFY_GETATTRLIST,
    ES_EVENT_TYPE_NOTIFY_GETEXTATTR,
    ES_EVENT_TYPE_NOTIFY_LISTEXTATTR,

    ES_EVENT_TYPE_NOTIFY_LOOKUP
 */

const es_event_type_t es_observed_events_[] =
{
    // Process life cycle
    ES_EVENT_TYPE_NOTIFY_EXEC,
    ES_EVENT_TYPE_NOTIFY_FORK,
    ES_EVENT_TYPE_NOTIFY_EXIT,

    // Read events
    
    ES_EVENT_TYPE_NOTIFY_CLOSE,

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
};

static os_log_t logger = os_log_create("com.microsoft.buildxl.sandbox", "Logger");

#define LOG_LINE "com_microsoft_buildxl_sandbox"
#define log(format, ...) os_log(logger, "[[ %s ]] %s: " #format "\n", LOG_LINE, __func__, __VA_ARGS__)
#define log_error(format, ...) os_log_error(logger, "[[ %s ]][ERROR] %s: " #format "\n", LOG_LINE, __func__, __VA_ARGS__)

#if DEBUG
#define log_debug(format, ...) log(format, __VA_ARGS__)
#else
#define log_debug(format, ...)
#endif

#endif /* ESConstants_h */
