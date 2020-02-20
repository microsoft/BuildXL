// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef ESSandbox_h
#define ESSandbox_h

#include <EndpointSecurity/EndpointSecurity.h>
#include <bsm/libbsm.h>
#include <map>

#include "BuildXLException.hpp"
#include "Common.h"
#include "SandboxedPip.hpp"
#include "SandboxedProcess.hpp"
#include "Trie.hpp"

#define ES_CLIENT_CREATION_FAILED       0x1
#define ES_CLIENT_CACHE_RESET_FAILED    0x2
#define ES_CLIENT_SUBSCRIBE_FAILED      0x4
#define ES_WRONG_BUFFER_SIZE            0x8
#define ES_INSTANCE_ERROR               0x16

typedef struct {
    int error;
} ESConnectionInfo;

static const dispatch_queue_attr_t processingQueuepriorityAttribute_ = dispatch_queue_attr_make_with_qos_class(
    DISPATCH_QUEUE_SERIAL, QOS_CLASS_USER_INITIATED, -1
);

const es_event_type_t observed_events_[] =
{
    // Process life cycle
    ES_EVENT_TYPE_NOTIFY_EXEC,
    ES_EVENT_TYPE_NOTIFY_FORK,
    ES_EVENT_TYPE_NOTIFY_EXIT,

    // ES_EVENT_TYPE_NOTIFY_OPEN,
    ES_EVENT_TYPE_NOTIFY_CLOSE,
    
    // Currently not used, maybe useful later
    // ES_EVENT_TYPE_NOTIFY_READDIR,
    // ES_EVENT_TYPE_NOTIFY_FSGETPATH,
    // ES_EVENT_TYPE_NOTIFY_DUP,

    // Read events
    ES_EVENT_TYPE_NOTIFY_READLINK,
    ES_EVENT_TYPE_NOTIFY_GETATTRLIST,
    ES_EVENT_TYPE_NOTIFY_GETEXTATTR,
    ES_EVENT_TYPE_NOTIFY_LISTEXTATTR,
    ES_EVENT_TYPE_NOTIFY_ACCESS,
    // ES_EVENT_TYPE_NOTIFY_STAT,

    // Write events
    ES_EVENT_TYPE_NOTIFY_CREATE,
    // ES_EVENT_TYPE_NOTIFY_WRITE,
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

    // ES_EVENT_TYPE_NOTIFY_LOOKUP
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
    
    pid_t pid_;
    
    std::map<pid_t, bool> whitelistedPids_;
    
    Trie<SandboxedProcess> *trackedProcesses_;
    
    es_client_t *client_;
    dispatch_queue_t processingQueue_;
    
    AccessReportCallback accessReportCallback_;
    
public:

    ESSandbox() = delete;
    ESSandbox(pid_t pid)
    {
        pid_ = pid;
        
        char queueName[PATH_MAX] = { '\0' };
        sprintf(queueName, "com.microsoft.buildxl.es.queue_%d", pid);
        processingQueue_ = dispatch_queue_create(queueName, processingQueuepriorityAttribute_);
        
        accessReportCallback_ = nullptr;
        trackedProcesses_ = Trie<SandboxedProcess>::createUintTrie();
        if (!trackedProcesses_)
        {
            throw BuildXLException("Could not create Trie for process tracking!");
        }
    }
    
    ~ESSandbox();
    
    inline const pid_t GetHostPid() const { return pid_; }
    
    inline std::map<pid_t, bool> GetPidMap() { return whitelistedPids_; }
    inline bool RemoveWhitelistedPid(pid_t pid)
    {
        auto result = whitelistedPids_.find(pid);
        if (result != whitelistedPids_.end())
        {
            return (whitelistedPids_.erase(pid) == 1);
        }
        
        return false;
    }
    
    inline const dispatch_queue_t GetProcessingQueue() const { return processingQueue_; }
    inline const AccessReportCallback GetAccessReportCallback() const { return accessReportCallback_; }
    inline const void SetAccessReportCallback(AccessReportCallback callback) { accessReportCallback_ = callback; }
    
    inline const es_client_t* GetESClient() const { return client_; }
    inline const void SetESClient(es_client_t *c) { client_ = c; }
    
    std::shared_ptr<SandboxedProcess> FindTrackedProcess(pid_t pid);
    bool TrackRootProcess(std::shared_ptr<SandboxedPip> pip);
    bool TrackChildProcess(pid_t childPid,std::shared_ptr<SandboxedProcess> parentProcess);
    bool UntrackProcess(pid_t pid, std::shared_ptr<SandboxedProcess> process);
    
    void const SendAccessReport(AccessReport &report, std::shared_ptr<SandboxedPip> pip);
};

#endif /* ESSandbox_h */
