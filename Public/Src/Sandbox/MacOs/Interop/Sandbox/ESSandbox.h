// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef ESSandbox_h
#define ESSandbox_h

#if ES_SANDBOX

#include <EndpointSecurity/EndpointSecurity.h>
#include <bsm/libbsm.h>

#include "Common.h"
#include "Trie.hpp"

#define ES_CLIENT_CREATION_FAILED       0x1
#define ES_CLIENT_CACHE_RESET_FAILED    0x2
#define ES_CLIENT_SUBSCRIBE_FAILED      0x4
#define ES_WRONG_BUFFER_SIZE            0x8

typedef struct {
    int error;
    uintptr_t client;
    uintptr_t source;
    uintptr_t runLoop;
} ESConnectionInfo;

enum class EventType {
    IOEvent,
    LookupEvent,
    ProcessEvent
};

extern "C"
{
    void InitializeEndpointSecuritySandbox(ESConnectionInfo *info, pid_t host);
    void DeinitializeEndpointSecuritySandbox(ESConnectionInfo info);
    
    __cdecl void ObserverFileAccessReports(ESConnectionInfo *info, AccessReportCallback callback, long accessReportSize);
};

bool ES_SendPipStarted(const pid_t pid, pipid_t pipId, const char *const famBytes, int famBytesLength);
bool ES_SendPipProcessTerminated(pipid_t pipId, pid_t pid);

class ESSandbox
{
private:
    
    Trie *trackedProcesses_;
    
    static const uint32_t numberOfSubscribedProcEvents_ = 3;
    const es_event_type_t proc_observed_events_[numberOfSubscribedProcEvents_] =
    {
        // Process life cycle
        ES_EVENT_TYPE_NOTIFY_EXEC,
        ES_EVENT_TYPE_NOTIFY_FORK,
        ES_EVENT_TYPE_NOTIFY_EXIT,
    };
    
    static const uint32_t numberOfSubscribedIOEvents_ = 13;
    const es_event_type_t io_observed_events_[numberOfSubscribedIOEvents_] =
    {
        // Process I/O operations
        
        // Currently deactivated, done through ES_EVENT_TYPE_NOTIFY_CLOSE to reduce overall event count
        // ES_EVENT_TYPE_NOTIFY_OPEN,
        
        ES_EVENT_TYPE_NOTIFY_CLOSE,
        ES_EVENT_TYPE_NOTIFY_CREATE,
        ES_EVENT_TYPE_NOTIFY_EXCHANGEDATA,
        ES_EVENT_TYPE_NOTIFY_RENAME,
        ES_EVENT_TYPE_NOTIFY_LINK,
        ES_EVENT_TYPE_NOTIFY_UNLINK,
        ES_EVENT_TYPE_NOTIFY_READLINK,
        ES_EVENT_TYPE_NOTIFY_WRITE,
        ES_EVENT_TYPE_NOTIFY_SETATTRLIST,
        ES_EVENT_TYPE_NOTIFY_SETEXTATTR,
        ES_EVENT_TYPE_NOTIFY_SETFLAGS,
        ES_EVENT_TYPE_NOTIFY_SETMODE,
        ES_EVENT_TYPE_NOTIFY_SETOWNER,
    };
            
    static const uint32_t numberOfSubscribedLookupEvents_ = 1;
    const es_event_type_t lookup_observed_events_[numberOfSubscribedLookupEvents_] =
    {
        ES_EVENT_TYPE_NOTIFY_LOOKUP
    };

    CFRunLoopSourceContext sourceContext_ = {
        .version         = 0,
        .info            = NULL,
        .retain          = NULL,
        .release         = NULL,
        .copyDescription = NULL,
        .equal           = NULL,
        .hash            = NULL,
        .schedule        = NULL,
        .cancel          = NULL,
        .perform         = NULL
    };
    
    es_client_t *client;
    es_handler_block_t esObservationHandler_;
    AccessReportCallback accessReportCallback_;
    
public:
    
    ESSandbox() = delete;
    ESSandbox(es_handler_block_t handler)
    {
        _Block_copy(handler);
        esObservationHandler_ = handler;
        accessReportCallback_ = nullptr;
        
        trackedProcesses_ = Trie::createUintTrie();
        if (!trackedProcesses_)
        {
            throw "Could not create Trie for process tracking!";
        }
    }
    
    ~ESSandbox();
    
    inline CFRunLoopSourceContext* GetRunLoopSourceContext() { return &sourceContext_; }
    
    inline es_event_type_t* GetEventsForType(EventType type)
    {
        switch (type)
        {
            case EventType::ProcessEvent:
                return (es_event_type_t*) proc_observed_events_;
            case EventType::IOEvent:
                return (es_event_type_t*) io_observed_events_;
            case EventType::LookupEvent:
                return (es_event_type_t*) lookup_observed_events_;
        }
    }
    
    inline int GetEventCountForType(EventType type)
    {
        switch (type)
        {
            case EventType::ProcessEvent:
                return numberOfSubscribedProcEvents_;
            case EventType::IOEvent:
                return numberOfSubscribedIOEvents_;
            case EventType::LookupEvent:
                return numberOfSubscribedLookupEvents_;
        }
    }
    
    inline es_handler_block_t GetObservationHandler() { return esObservationHandler_; }
    
    inline const AccessReportCallback GetAccessReportCallback() { return accessReportCallback_; }
    inline const void SetAccessReportCallback(AccessReportCallback callback) { accessReportCallback_ = callback; }
    
    inline const es_client_t* GetESClient() { return client; }
    inline const void SetESClient(es_client_t *c) { client = c; }
    
    SandboxedProcess* FindTrackedProcess(pid_t pid);
    bool TrackRootProcess(SandboxedPip *pip);
    bool TrackChildProcess(pid_t childPid, SandboxedProcess *parentProcess);
    bool UntrackProcess(pid_t pid, SandboxedProcess *process);
    
    void const SendAccessReport(AccessReport &report, SandboxedPip *pip);
};

#else  /* ES_SANDBOX */

#include "BuildXLSandboxShared.hpp"

bool ES_SendPipStarted(const pid_t pid, pipid_t pipId, const char *const famBytes, int famBytesLength) {
    return false;
}
bool ES_SendPipProcessTerminated(pipid_t pipId, pid_t pid) {
    return false;
}

#endif /* ES_SANDBOX */

#endif /* ESSandbox_h */
