// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef BuildXLSandbox_hpp
#define BuildXLSandbox_hpp

#include <IOKit/IOService.h>
#include <sys/kauth.h>

#include "AutoRelease.hpp"
#include "Listeners.hpp"
#include "BuildXLSandboxShared.hpp"
#include "ConcurrentDictionary.hpp"
#include "ClientInfo.hpp"
#include "ResourceManager.hpp"
#include "SandboxedProcess.hpp"

#if RELEASE
    #define kSharedDataQueueSizeDefault 256
#else
    #define kSharedDataQueueSizeDefault 16
#endif

#define kSharedDataQueueSizeMax 2048

#define AddTimeStampToAccessReport(report, struct_property)\
do { (report)->stats.struct_property = mach_absolute_time(); }while(0);

class BuildXLSandbox : public IOService
{
    OSDeclareDefaultStructors(BuildXLSandbox)

private:

    kauth_listener_t buildxlFileOpListener_ = nullptr;
    kauth_listener_t buildxlVnodeListener_ = nullptr;

    mac_policy_handle_t policyHandle_;
    struct mac_policy_ops buildxlPolicyOps_;
    struct mac_policy_conf policyConfiguration_;

    AllCounters counters_;

    /*!
     * A dictionary (PID -> ClientInfo*) keeping track of connected clients.
     * The key in the dictionary is the process id of the connected client.
     */
    Trie *connectedClients_;

    /*!
     * Configuration.
     *
     * TODO: different clients can have different configurations, hence this should be kept per client.
     */
    KextConfig config_;

    /*!
     * Used for managing fork throttling.
     *
     * TODO: this should be kept per client.
     */
    ResourceManager *resourceManager_;

    /*! Recursive lock used for synchronization */
    IORecursiveLock *lock_;

    /*!
     * Keeps the PID --> SandboxedProcess* mapping of currently tracked processes.
     *
     * This dictionary is used in the following scenarios:
     *
     *   - when a pip is started (kBuildXLSandboxActionSendPipStarted is received)
     *     a new ProcessObject instance is created and remembered here;
     *
     *   - when a tracked process spawns a child process, the child process is added here too;
     *
     *   - on EVERY file accesses (e.g., from KAuth and TrustedBSD handlers) this
     *     dictionary is consulted to see if the process requesting the file access
     *     is being tracked, hence, a VERY EFFICIENT implementation of utmost importance;
     *
     *   - when a tracked process exits the process is removed from this dictionary.
     */
    Trie *trackedProcesses_;

    ClientInfo* GetClientInfo(pid_t clientPid);

    void InitializePolicyStructures();

    void OnLastClientDisconnected();
    bool InitializeTries();

public:

    typedef enum { kTrackingNew, kAlreadyTrackedOk, kAlreadyTrackedBad, kTrackFailed } TrackChildProcessResult;

    bool init(OSDictionary *dictionary = 0) override;
    void free(void) override;

    bool start(IOService *provider) override;
    void stop(IOService *provider) override;

    void Configure(const KextConfig *config);
    inline const KextConfig GetConfig() { return config_; }

    UInt32 GetReportQueueEntryCount();

    IOReturn AllocateNewClient(pid_t clientPid);
    IOReturn DeallocateClient(pid_t clientPid);

    IOReturn InitializeListeners();
    void UninitializeListeners();

    AllCounters* Counters()           { return &counters_; }
    ResourceManager* ResourceManger() { return resourceManager_; }

    inline void ResetCounters()
    {
        counters_ = {0};
    }

    /*!
     * Sets the notification port for the shared data queue for the client process 'pid'.
     */
    IOReturn SetReportQueueNotificationPort(mach_port_t port, pid_t pid);

    /*!
     * Returns a newly allocated memory descriptor of the shared data queue for the client process 'pid'.
     *
     * NOTE: the caller is responsible for releasing the returned object.
     */
    IOMemoryDescriptor* const GetReportQueueMemoryDescriptor(pid_t pid);

    /*!
     * Send the access report to only one queue using the round robin strategy
     */
    bool const SendAccessReport(AccessReport &report, SandboxedPip *pip, const CacheRecord *cacheRecord);

#pragma mark Client Failure Notification Mapping

    /*!
     * Sets the async failure handle for the shared data queue belonging to client process 'pid'
     */
    inline IOReturn SetFailureNotificationHandlerForClientPid(pid_t pid, OSAsyncReference64 ref, OSObject *client)
    {
        EnterMonitor

        ClientInfo *info = connectedClients_->getAs<ClientInfo>(pid);

        bool success =
            info != nullptr &&
            info->setFailureNotificationHandler(ref, client);

        return success ? kIOReturnSuccess : kIOReturnError;
    }

#pragma mark Process Tracking

    /*!
     * Starts tracking process, including any of the children processes it may spawn.
     *
     * This operation corresponds to a client explicitly requesting to track a process.
     */
    bool TrackRootProcess(SandboxedPip *pip);

    /*!
     * Starts tracking a process that is a child of an already tracked process.
     *
     * This operation is invoked internally when this kernel extension detects that an
     * already tracked process has forked and spawned a child process.
     */
    bool TrackChildProcess(pid_t childPid, SandboxedProcess *parentProcess);

    /*!
     * Stops tracking process 'pid'.  The following precondition must hold:
     *
     *   FindTrackedProcess(pid) == process
     */
    bool UntrackProcess(pid_t pid, SandboxedProcess *process);

    /*!
     * Returns a SandboxedProcess pointer corresponding to 'pid' if such process is being tracked.
     */
    SandboxedProcess* FindTrackedProcess(pid_t pid);

    /*!
     * Introspect the current state of the sandbox.
     */
    IntrospectResponse Introspect() const;
};

#endif /* BuildXLSandbox_hpp */
