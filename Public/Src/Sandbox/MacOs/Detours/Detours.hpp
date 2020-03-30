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

static const char* socket_path = "/tmp/buildxl_interpose";

static os_log_t logger = os_log_create(DETOURS_BUNDLE_IDENTIFIER, "Logger");

typedef struct {
    char data[PATH_MAX] = { '\0' };
    size_t length;
} Buffer;

struct PathCacheEntry final
{
private:
    Buffer buffer_;
public:
    PathCacheEntry(int identifier, bool isPid = false)
    {
        assert(identifier > 0);
        if (!isPid)
        {
            assert(fcntl(identifier, F_GETPATH, buffer_.data) != -1);
        }
        else
        {
            assert(proc_pidpath(identifier, (void *)buffer_.data, PATH_MAX) > 0);
        }
        buffer_.length = strlen(buffer_.data);
    }
    
    ~PathCacheEntry() = default;
    inline const char* GetPath() const { return buffer_.data; }
};

#define log(format, ...) os_log(logger, "[[ %s ]] %s: " #format "\n", "com_microsoft_buildxl_detours", __func__, __VA_ARGS__)

#if DEBUG
#define log_debug(format, ...) log(format, __VA_ARGS__)
#else
#define log_debug(format, ...)
#endif

#define DYLD_INTERPOSE(_replacment,_replacee) \
    __attribute__ ((used)) static struct{ const void* replacment; const void* replacee; } _interpose_##_replacee \
    __attribute__ ((section ("__DATA,__interpose"))) = { (const void*)(unsigned long)&_replacment, (const void*)(unsigned long)&_replacee };

#define DEFAULT_EVENT_CONSTRUCTOR(type, src, dst, mode) \
    int old_errno = errno; \
    IOEvent event(getpid(), 0, getppid(), type, src, dst, get_executable_path(getpid()), mode); \
    send_to_sandbox(event, type); \
    errno = old_errno; \
    return result;

#define EXEC_EVENT_CONSTRUCTOR(path) \
    IOEvent event(getpid(), 0, getppid(), ES_EVENT_TYPE_NOTIFY_EXEC, path, "", path, false); \
    send_to_sandbox(event, ES_EVENT_TYPE_NOTIFY_EXEC);

#define FORK_EVENT_CONSTRUCTOR(result, child_pid, pid, ppid, cmp) \
    int old_errno = errno; \
    if (result cmp 0) { \
        std::string fullpath = get_executable_path(*child_pid); \
        IOEvent event(pid, *child_pid, ppid, ES_EVENT_TYPE_NOTIFY_FORK, "", "", fullpath, false); \
        send_to_sandbox(event, ES_EVENT_TYPE_NOTIFY_FORK); \
    } \
    errno = old_errno; \
    return result;

#define STAT_EVENT_CONSTRUCTOR(type, src) \
    int old_errno = errno; \
    IOEvent event(getpid(), 0, getppid(), type, src, "", get_executable_path(getpid()), s->st_mode); \
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
            IOEvent event(getpid(), 0, getppid(), ES_EVENT_TYPE_NOTIFY_WRITE, path, dst, get_executable_path(getpid())); \
            send_to_sandbox(event, ES_EVENT_TYPE_NOTIFY_WRITE); \
        } \
    } \
    errno = old_errno; \
    return result;

#endif /* Detours_hpp */
