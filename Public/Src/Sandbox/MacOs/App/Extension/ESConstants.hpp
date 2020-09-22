// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef ESConstants_h
#define ESConstants_h

#include <os/log.h>
#include "IOEvent.hpp"

static os_log_t logger = os_log_create("com.microsoft.buildxl.sandbox", "Logger");

#define LOG_LINE "com_microsoft_buildxl_sandbox"
#define log(format, ...) os_log(logger, "[[ %s ]] %s: " #format "\n", LOG_LINE, __func__, __VA_ARGS__)
#define log_error(format, ...) os_log_error(logger, "[[ %s ]][ERROR] %s: " #format "\n", LOG_LINE, __func__, __VA_ARGS__)

#if DEBUG
#define log_debug(format, ...) log(format, __VA_ARGS__)
#else
#define log_debug(format, ...)
#endif

/*

 Currently the following events are not hooked up, maybe useful for later:

    ES_EVENT_TYPE_NOTIFY_READDIR,
    ES_EVENT_TYPE_NOTIFY_FSGETPATH,
    ES_EVENT_TYPE_NOTIFY_DUP,
    ES_EVENT_TYPE_NOTIFY_WRITE, // Slows down ES due to callback being invoked on every write on block size bytes

 */

const es_event_type_t es_lifetime_events_[] =
{
    ES_EVENT_TYPE_AUTH_EXEC,
    ES_EVENT_TYPE_NOTIFY_FORK
};

const es_event_type_t es_exit_events_[] =
{
    ES_EVENT_TYPE_NOTIFY_EXIT
};

const es_event_type_t es_write_events_[] =
{
    ES_EVENT_TYPE_AUTH_CREATE,
    ES_EVENT_TYPE_AUTH_TRUNCATE,
    ES_EVENT_TYPE_AUTH_CLONE,
    ES_EVENT_TYPE_AUTH_EXCHANGEDATA,
    ES_EVENT_TYPE_AUTH_RENAME,

    ES_EVENT_TYPE_AUTH_LINK,
    ES_EVENT_TYPE_AUTH_UNLINK,

    ES_EVENT_TYPE_AUTH_SETATTRLIST,
    ES_EVENT_TYPE_AUTH_SETEXTATTR,
    ES_EVENT_TYPE_AUTH_DELETEEXTATTR,
    ES_EVENT_TYPE_AUTH_SETFLAGS,
    ES_EVENT_TYPE_AUTH_SETMODE,
    ES_EVENT_TYPE_AUTH_SETOWNER,
    ES_EVENT_TYPE_AUTH_SETACL
};

const es_event_type_t es_read_events_[] =
{
    ES_EVENT_TYPE_AUTH_OPEN,
    ES_EVENT_TYPE_NOTIFY_ACCESS,
    ES_EVENT_TYPE_AUTH_READLINK
};

const es_event_type_t es_probe_events_[] =
{
    ES_EVENT_TYPE_NOTIFY_STAT,
    ES_EVENT_TYPE_AUTH_GETATTRLIST,
    ES_EVENT_TYPE_AUTH_GETEXTATTR,
    ES_EVENT_TYPE_AUTH_LISTEXTATTR
};

const es_event_type_t es_lookup_events_[] =
{
//    ES_EVENT_TYPE_NOTIFY_LOOKUP
};

#endif /* ESConstants_h */
