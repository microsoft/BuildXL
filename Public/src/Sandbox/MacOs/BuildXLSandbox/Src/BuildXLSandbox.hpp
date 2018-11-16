//
//  BuildXLSandbox.hpp
//  BuildXLSandbox
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#ifndef BuildXLSandbox_hpp
#define BuildXLSandbox_hpp

#include <IOKit/IOService.h>
#include <sys/kauth.h>

#include "Listeners.hpp"
#include "BuildXLSandboxShared.hpp"
#include "ConcurrentDictionary.hpp"
#include "ConcurrentMultiplexingQueue.hpp"
#include "ConcurrentSharedDataQueue.hpp"
#include "ProcessObject.hpp"

#define BuildXLSandbox com_microsoft_buildXL_Sandbox

#if RELEASE
    #define kSharedDataQueueSizeDefault 256
#else
    #define kSharedDataQueueSizeDefault 16
#endif

#define kSharedDataQueueSizeMax 2048
#define kProcessDictionaryCapacity 1024

class BuildXLSandbox : public IOService
{
    OSDeclareDefaultStructors(BuildXLSandbox)

private:

    kauth_listener_t buildXLFileOpListener_ = nullptr;
    kauth_listener_t buildXLVnodeListener_ = nullptr;

    mac_policy_handle_t policyHandle_;
    struct mac_policy_ops buildXLPolicyOps_;
    struct mac_policy_conf policyConfiguration_;

    /*!
     * Used to manage multiple shared data queues per client
     */
    ConcurrentMultiplexingQueue *reportQueues_;

    /*!
     * Used to manage the report queue size client
     */
    UInt32 reportQueueSize_;

    /*! Recursive lock used for synchronization */
    IORecursiveLock *lock_;

    /*!
     * Keeps the PID --> ProcessObject* mapping of currently tracked processes.
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
     *     is being tracked, hence, a VERY EFFICIENT implementation of the
     *     ConcurrentDictionary::findProcess instance method is of utmost importance;
     *
     *   - when a tracked process exits the process is removed from this dictionary.
     */
    ConcurrentDictionary *trackedProcesses_;

    void InitializePolicyStructures();
    kern_return_t InitializeListeners();
    void UninitializeListeners();

    bool const SendFileAccessReport(pid_t clientPid, AccessReport &report, bool roundRobin);

public:

    bool verboseLoggingEnabled = false;

    bool init(OSDictionary *dictionary = 0) override;
    void free(void) override;

    bool start(IOService *provider) override;
    void stop(IOService *provider) override;

    void SetReportQueueSize(UInt32 reportQueueSize);
    UInt32 GetReportQueueEntryCount();

    IOReturn AllocateReportQueueForClientProcess(pid_t pid);
    IOReturn FreeReportQueuesForClientProcess(pid_t pid);

    /*!
     * Sets the notification port of the next queue in the connected clients queues-bucket (reportQueues_)
     * that has no notifaction port set yet.
     */
    IOReturn SetReportQueueNotificationPort(mach_port_t port, pid_t pid);

    /*!
     * Gets a valid memory descriptor of the next queue in the connected clients queues-bucket (reportQueues_)
     * that has not been queried for a memory descriptor yet.
     */
    IOMemoryDescriptor* const GetReportQueueMemoryDescriptor(pid_t pid);

    /*!
     * Send the access report to only one queue using the round robin strategy
     */
    inline bool const SendAccessReport(pid_t clientPid, AccessReport &report)
    {
        return SendFileAccessReport(clientPid, report, /*roundRobin*/ true);
    }

    /*!
     * Send the access report to all the registered queues.
     */
    inline bool const BroadcastAccessReport(pid_t clientPid, AccessReport &report)
    {
        return SendFileAccessReport(clientPid, report, /*roundRobin*/ false);
    }

#pragma mark Client Failure Notification Mapping

    /*!
     * Sets the async reference callback handle on all queues belonging to a specific user client
     */
    inline IOReturn SetFailureNotificationHandlerForClientPid(pid_t pid, OSAsyncReference64 ref, OSObject *client)
    {
        EnterMonitor

        const OSSymbol *key = ProcessObject::computePidHashCode(pid);
        bool result = reportQueues_->setFailureNotificationHandlerForAllQueues(key, ref, client);
        OSSafeReleaseNULL(key);

        return result ? kIOReturnSuccess : kIOReturnError;
    }

#pragma mark Process Tracking

    /*!
     * Starts tracking process, including any of the children processes it may spawn.
     *
     * This operation corresponds to a client explicitly requesting to track a process.
     */
    bool TrackRootProcess(const ProcessObject *rootProcess);

    /*!
     * Starts tracking a process that is a child of an already tracked process.
     *
     * This operation is invoked internally when this kernel extension detects that an
     * already tracked process has forked and spawned a child process.
     */
    bool TrackChildProcess(pid_t childPid, ProcessObject *rootProcess);

    /*!
     * Stops tracking process 'pid' when its pip id matches a supplied one
     *
     * @param pid :: process id of the process to stop tracking
     * @param expectedPipId :: condition under which to stop tracking process (only when its pip id matches this value);
     *                         passing -1 overrides this condition check.
     * @result :: returns True if there was a process with process id 'pid' matching 'expectedPipId' and False otherwise
     */
    bool UntrackProcess(pid_t pid, pipid_t expectedPipId = -1);

    /*!
     * Stops tracking process 'pid'.  'rootProcess' must be a parent of 'pid'
     * that has been explicitly requested to be tracked, i.e., the following
     * precondition must hold:
     *
     *   FindTrackedProcess(pid) == rootProcess.
     */
    void UntrackProcess(pid_t pid, ProcessObject *rootProcess);

    /*!
     * Returns a ProcessObject pointer corresponding to 'pid' if such process is being tracked.
     *
     * NOTE that 'getProcessId()' of the result doesn't have to be equal to 'pid',
     */
    ProcessObject* FindTrackedProcess(pid_t pid);
};

#endif /* BuildXLSandbox_hpp */
