// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef ESSandbox_h
#define ESSandbox_h

#ifdef ES_SANDBOX

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
    
    /*! This is the number of observed ES events, keep this up to date with the active events in observed_events_ otherwise ES behaves very odd! */
    static const int numberOfSubscribedEvents_ = 18;
    
    const es_event_type_t observed_events_[numberOfSubscribedEvents_] =
    {
        // Process life cycle
        ES_EVENT_TYPE_NOTIFY_EXEC,
        ES_EVENT_TYPE_NOTIFY_FORK,
        ES_EVENT_TYPE_NOTIFY_EXIT,

        // Process file operations
        ES_EVENT_TYPE_NOTIFY_OPEN,
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
        
          // Crashes with segmentation faults and assert violations frequently with serious workload (radar created)
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
    
    inline es_event_type_t* GetSubscibedESEvents() { return (es_event_type_t*) observed_events_; }
    inline int GetSubscribedESEventsCount() { return numberOfSubscribedEvents_; }
    
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
