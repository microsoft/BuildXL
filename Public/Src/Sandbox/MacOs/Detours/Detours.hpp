// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef Detours_hpp
#define Detours_hpp

#include <assert.h>
#include <libproc.h>
#include <os/log.h>
#include <spawn.h>
#include <stdio.h>
#include <string.h>
#include <sys/clonefile.h>
#include <sys/fcntl.h>
#include <sys/fsgetpath.h>

#include <EndpointSecurity/EndpointSecurity.h>

#include "IOEvent.hpp"

#define DETOURS_BUNDLE_IDENTIFIER "com.microsoft.buildxl.detours"

#define DYLD_INTERPOSE(_replacment,_replacee) \
    __attribute__ ((used)) static struct{ const void* replacment; const void* replacee; } _interpose_##_replacee \
    __attribute__ ((section ("__DATA,__interpose"))) = { (const void*)(unsigned long)&_replacment, (const void*)(unsigned long)&_replacee };

#define DEFAULT_EVENT_CONSTRUCTOR(type, src, dst, mode) \
    int old_errno = errno; \
    IOEvent event(getpid(), 0, getppid(), type, ES_ACTION_TYPE_NOTIFY, src, dst, get_executable_path(getpid()), mode); \
    send_to_sandbox(event, type); \
    errno = old_errno; \
    return result;

#define DEFAULT_EVENT_CONSTRUCTOR_NO_RESOLVE(type, src, dst, mode, report) \
    int old_errno = errno; \
    if (report) { \
        IOEvent event(getpid(), 0, getppid(), type, ES_ACTION_TYPE_NOTIFY, src, dst, get_executable_path(getpid()), true); \
        send_to_sandbox(event, type, false, false); \
    } \
    errno = old_errno; \
    return result;

#define EXEC_EVENT_CONSTRUCTOR(path) \
    IOEvent event(getpid(), 0, getppid(), ES_EVENT_TYPE_NOTIFY_EXEC, ES_ACTION_TYPE_NOTIFY, path, "", get_executable_path(getpid()), false); \
    send_to_sandbox(event, ES_EVENT_TYPE_NOTIFY_EXEC, true);\

#define EXIT_EVENT_CONSTRUCTOR() \
    IOEvent event(getpid(), 0, getppid(), ES_EVENT_TYPE_NOTIFY_EXIT, ES_ACTION_TYPE_NOTIFY, "", "", get_executable_path(getpid()), false); \
    send_to_sandbox(event, ES_EVENT_TYPE_NOTIFY_EXIT);

#define FORK_EVENT_CONSTRUCTOR(result, child_pid, pid, ppid, cmp) \
    int old_errno = errno; \
    if (result cmp 0) { \
        std::string fullpath = get_executable_path(*child_pid); \
        IOEvent event(pid, *child_pid, ppid, ES_EVENT_TYPE_NOTIFY_FORK, ES_ACTION_TYPE_NOTIFY, "", "", fullpath, false); \
        send_to_sandbox(event, ES_EVENT_TYPE_NOTIFY_FORK); \
    } \
    errno = old_errno; \
    return result;

#define STAT_EVENT_CONSTRUCTOR(type, src) \
    int old_errno = errno; \
    IOEvent event(getpid(), 0, getppid(), type, ES_ACTION_TYPE_NOTIFY, src, "", get_executable_path(getpid()), s->st_mode); \
    send_to_sandbox(event, type); \
    errno = old_errno; \
    return result;

#define WRITE_EVENT_CONSTRUCTOR(path, dst) \
    int old_errno = errno; \
    if (success == 0) { \
        bool reported = trackedPaths_->get(path) != nullptr; \
        if (!reported) { \
            std::shared_ptr<PathCacheEntry> entry(new PathCacheEntry(fildes)); \
            trackedPaths_->insert(path, entry); \
            IOEvent event(getpid(), 0, getppid(), ES_EVENT_TYPE_NOTIFY_WRITE, ES_ACTION_TYPE_NOTIFY, path, dst, get_executable_path(getpid())); \
            send_to_sandbox(event, ES_EVENT_TYPE_NOTIFY_WRITE); \
        } \
    } \
    errno = old_errno; \
    return result;

#endif /* Detours_hpp */
