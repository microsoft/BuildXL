//
//  BuildXLSandbox.cpp
//  DominoSandbox
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#include <kern/clock.h>

#include "BuildXLSandbox.hpp"
#include "TrustedBsdHandler.hpp"
#include "Listeners.hpp"

#define super IOService

OSDefineMetaClassAndStructors(DominoSandbox, IOService)

os_log_t logger = os_log_create(kBuildXLBundleIdentifier, "Logger");

bool DominoSandbox::init(OSDictionary *dictionary)
{
    if (!super::init(dictionary))
    {
        return false;
    }

    lock_ = IORecursiveLockAlloc();
    if (!lock_)
    {
        return false;
    }

    reportQueueSize_ = kSharedDataQueueSizeDefault;
    reportQueues_ = ConcurrentMultiplexingQueue::Create();
    if (!reportQueues_)
    {
        return false;
    }

    trackedProcesses_ = ConcurrentDictionary::withCapacity(kProcessDictionaryCapacity, "TrackedProcesses");
    if (!trackedProcesses_)
    {
        return false;
    }

    kern_return_t result = InitializeListeners();
    if (result != KERN_SUCCESS)
    {
        return false;
    }

    return true;
}

void DominoSandbox::free(void)
{
    UninitializeListeners();

    if (lock_)
    {
        IORecursiveLockFree(lock_);
        lock_ = nullptr;
    }

    OSSafeReleaseNULL(trackedProcesses_);
    OSSafeReleaseNULL(reportQueues_);

    super::free();
}

bool DominoSandbox::start(IOService *provider)
{
    bool success = super::start(provider);
    if (success)
    {
        registerService();
    }

    return success;
}

void DominoSandbox::stop(IOService *provider)
{
    super::stop(provider);
}

void DominoSandbox::InitializePolicyStructures()
{
    policyHandle_ = {0};

    Listeners::g_dispatcher = this;

    dominoPolicyOps_ =
    {
        // NOTE: handle preflight instead of mpo_vnode_check_lookup because trying to get the path for a vnode
        //       (vn_getpath) inside of that handler overwhelms the system very quickly
        .mpo_vnode_check_lookup_preflight = Listeners::mpo_vnode_check_lookup_pre,

        // this event happens right after fork only on the child processes
        .mpo_cred_label_associate_fork    = Listeners::mpo_cred_label_associate_fork,

        // some tools spawn child processes using execve() and vfork(), while this is non standard, we have to handle it
        // especially for shells like csh / tcsh
        .mpo_cred_label_update_execve     = Listeners::mpo_cred_label_update_execve,

        .mpo_vnode_check_exec             = Listeners::mpo_vnode_check_exec,

        .mpo_proc_notify_exit             = Listeners::mpo_proc_notify_exit,

        .mpo_vnode_check_create           = Listeners::mpo_vnode_check_create,

        .mpo_vnode_check_readlink         = Listeners::mpo_vnode_check_readlink,
    };

    policyConfiguration_ =
    {
        .mpc_name            = kBuildXLSandboxClassName,
        .mpc_fullname        = "Sandbox for process liftetime, I/O observation and control",
        .mpc_labelnames      = NULL,
        .mpc_labelname_count = 0,
        .mpc_ops             = &dominoPolicyOps_,
        .mpc_loadtime_flags  = MPC_LOADTIME_FLAG_UNLOADOK,
        .mpc_field_off       = NULL,
        .mpc_runtime_flags   = 0,
        .mpc_list            = NULL,
        .mpc_data            = NULL
    };
}

kern_return_t DominoSandbox::InitializeListeners()
{
    InitializePolicyStructures();
    kern_return_t status = mac_policy_register(&policyConfiguration_, &policyHandle_, NULL);
    if (status != KERN_SUCCESS)
    {
        log_error("Registering TrustedBSD MAC policy callbacks failed with error code %#X", status);
        return status;
    }

    dominoVnodeListener_ = kauth_listen_scope(KAUTH_SCOPE_VNODE, Listeners::domino_vnode_listener, reinterpret_cast<void *>(this));
    if (dominoVnodeListener_ == nullptr)
    {
        log_error("%s", "Registering callback for KAUTH_SCOPE_VNODE scope failed!");
        return KERN_FAILURE;
    }

    dominoFileOpListener_ = kauth_listen_scope(KAUTH_SCOPE_FILEOP, Listeners::domino_file_op_listener, reinterpret_cast<void *>(this));
    if (dominoFileOpListener_ == nullptr)
    {
        log_error("%s", "Registering callback for KAUTH_SCOPE_FILEOP scope failed!");
        return KERN_FAILURE;
    }

    return KERN_SUCCESS;
}

void DominoSandbox::UninitializeListeners()
{
    if (dominoVnodeListener_ != nullptr)
    {
        kauth_unlisten_scope(dominoVnodeListener_);
        log_debug("%s", "Deregistered callback for KAUTH_SCOPE_VNODE scope");
        dominoVnodeListener_ = nullptr;
    }

    if (dominoFileOpListener_ != nullptr)
    {
        kauth_unlisten_scope(dominoFileOpListener_);
        log_debug("%s", "Deregistered callback for KAUTH_SCOPE_FILEOP scope");
        dominoFileOpListener_ = nullptr;
    }

    mac_policy_unregister(policyHandle_);
    log_debug("%s", "Deregistered TrustedBSD MAC policy callbacks");
}

void DominoSandbox::SetReportQueueSize(UInt32 reportQueueSize)
{
    reportQueueSize_ = (reportQueueSize == 0 || reportQueueSize > kSharedDataQueueSizeMax) ? kSharedDataQueueSizeDefault : reportQueueSize;
    log_debug("Size set to: %u", reportQueueSize_);
}

UInt32 DominoSandbox::GetReportQueueEntryCount()
{
    return (reportQueueSize_ * 1024 * 1024) / sizeof(AccessReport);
}

IOReturn DominoSandbox::AllocateReportQueueForClientProcess(pid_t pid)
{
    EnterMonitor

    const OSSymbol *key = ProcessObject::computePidHashCode(pid);
    auto *queue = ConcurrentSharedDataQueue::withEntries(GetReportQueueEntryCount(), sizeof(AccessReport));
    bool success = reportQueues_->insertQueue(key, queue);
    OSSafeReleaseNULL(queue);
    OSSafeReleaseNULL(key);

    return success ? kIOReturnSuccess : kIOReturnError;
}

typedef struct {
    pid_t clientPid;
    OSArray *pidsToRemove;
} ReleaseContext;

IOReturn DominoSandbox::FreeReportQueuesForClientProcess(pid_t clientPid)
{
    EnterMonitor

    const OSSymbol *key = ProcessObject::computePidHashCode(clientPid);
    reportQueues_->removeQueues(key);
    OSSafeReleaseNULL(key);

    log_debug("Freed report queues for client PID(%d), remaining report queue mappings in wired memory: %d",
              clientPid, reportQueues_->getBucketCount());

    // Make sure to also cleanup any remaining tracked process objects as the client could have exited abnormally (crashed)
    // and we don't want those objects to stay around any longer
    
    ReleaseContext ctx = {
        .clientPid = clientPid,
        .pidsToRemove = OSArray::withCapacity(10)
    };

    if (ctx.pidsToRemove == nullptr)
    {
        log_error("Could not allocate a collection for removal of dangling processes for Client PID %d", clientPid);
        return kIOReturnError;
    }

    // find processes to untrack
    trackedProcesses_->forEach(&ctx, [](void *data, int index, const OSSymbol *key, const OSObject *value)
    {
        ReleaseContext *context = (ReleaseContext *)data;
        ProcessObject *process = OSDynamicCast(ProcessObject, value);
        if (context != nullptr && process != nullptr && process->getClientPid() == context->clientPid)
        {
            context->pidsToRemove->setObject(context->pidsToRemove->getCount(), process->getHashCode());
        }
    });

    // untrack and remove found processes
    for (int i = 0; i < ctx.pidsToRemove->getCount(); i++)
    {
        const OSSymbol *pidSym = OSDynamicCast(OSSymbol, ctx.pidsToRemove->getObject(i));
        bool removed = trackedProcesses_->remove(pidSym);
        log_debug("Remove tracked process PID(%s) for client process PID(%d) on cleanup: %s",
                  pidSym->getCStringNoCopy(), clientPid, removed ? "Removed" : "Not found");
    }

    OSSafeReleaseNULL(ctx.pidsToRemove);
    return kIOReturnSuccess;
}

IOReturn DominoSandbox::SetReportQueueNotificationPort(mach_port_t port, pid_t pid)
{
    EnterMonitor

    const OSSymbol *key = ProcessObject::computePidHashCode(pid);
    bool success = reportQueues_->setNotifactonPortForNextQueue(key, port);
    OSSafeReleaseNULL(key);

    return success ? kIOReturnSuccess : kIOReturnError;
}

IOMemoryDescriptor* const DominoSandbox::GetReportQueueMemoryDescriptor(pid_t pid)
{
    EnterMonitor

    const OSSymbol *key = ProcessObject::computePidHashCode(pid);
    IOMemoryDescriptor* descriptor = reportQueues_->getMemoryDescriptorForNextQueue(key);
    OSSafeReleaseNULL(key);

    return descriptor;
}

bool const DominoSandbox::SendFileAccessReport(pid_t clientPid, AccessReport &report, bool roundRobin)
{
    EnterMonitor

    const OSSymbol *key = ProcessObject::computePidHashCode(clientPid);

    AddTimeStampToAccessReport(&report, enqueueTime);
    bool success = reportQueues_->enqueueData(key, &report, sizeof(report), roundRobin);

    OSSafeReleaseNULL(key);

    log_error_or_debug(verboseLoggingEnabled, !success,
                       "DominoSandbox::SendFileAccessReport ClientPID(%d), PID(%d), Root PID(%d), PIP(%#llX), Operation: %s, Path: %s, Status: %d, Sent: %s",
                       clientPid, report.pid, report.rootPid, report.pipId, OpNames[report.operation], report.path, report.status, success ? "succeeded" : "failed");

    return success;
}

ProcessObject* DominoSandbox::FindTrackedProcess(pid_t pid)
{
    // NOTE: this has to be very fast when we are not tracking any processes (i.e., trackedProcesses_ is empty)
    //       because this is called on every single file access any process makes
    return trackedProcesses_->getProcess(pid);
}

bool DominoSandbox::TrackRootProcess(const ProcessObject *process, const uint64_t callbackInvocationTime)
{
    EnterMonitor

    pid_t pid = process->getProcessId();

    // if mapping for 'pid' exists --> remove it (this can happen only if clients are nested)
    TrustedBsdHandler handler = TrustedBsdHandler(this);
    if (handler.TryInitializeWithTrackedProcess(pid))
    {
        handler.HandleProcessUntracked(pid);
        log_verbose(verboseLoggingEnabled, "Untracking process PID = %d early, parent PID = %d, tree size = %d",
                    pid, handler.GetProcessId(), handler.GetProcessTreeSize());
    }

    bool inserted = trackedProcesses_->insertProcess(process);
    log_verbose(verboseLoggingEnabled, "Tracking top process PID = %d; inserted: %d", pid, inserted);
    return inserted;
}

bool DominoSandbox::TrackChildProcess(pid_t childPid, ProcessObject *rootProcess)
{
    EnterMonitor

    ProcessObject *existingProcess = trackedProcesses_->getProcess(childPid);
    if (existingProcess)
    {
        log_debug("Child process PID(%d) already tracked; existing: Root PID(%d), intended new: Root PID(%d)",
                  childPid, existingProcess->getProcessId(), rootProcess->getProcessId());

        if (existingProcess->getPipId() != rootProcess->getPipId() &&
            existingProcess->getClientPid() != rootProcess->getClientPid())
        {
            log_error("Found existing child process (PipId: %#llX / ClientId: %d) that does not match its root process data (PipId: %#llX / ClientId: %d)",
                      existingProcess->getPipId(), existingProcess->getClientPid(), rootProcess->getPipId(), rootProcess->getClientPid());
        }

        return false;
    }

    const OSSymbol *childPidKey = ProcessObject::computePidHashCode(childPid);

    // add the child process to process tree
    trackedProcesses_->insert(childPidKey, rootProcess);
    rootProcess->incrementProcessTreeCount();
    log_verbose(verboseLoggingEnabled, "Tracking child process PID = %d; parent: %d (tree size = %d)",
                childPid, rootProcess->getProcessId(), rootProcess->getProcessTreeCount());

    OSSafeReleaseNULL(childPidKey);
    return true;
}

bool DominoSandbox::UntrackProcess(pid_t pid, ProcessObject *rootProcess)
{
    EnterMonitor

    log_verbose(verboseLoggingEnabled, "Untracking entry %d --> %d (PipId: %#llX, process tree count: %d)",
                pid, rootProcess->getProcessId(), rootProcess->getPipId(), rootProcess->getProcessTreeCount());

    // remove the mapping for 'pid'
    if (!trackedProcesses_->removeProcess(pid))
    {
        log_error("Process with PID = %d not found in tracked processes", pid);
        return false;
    }
    else
    {
        rootProcess->decrementProcessTreeCount();
        return true;
    }
}

typedef struct {
    OSDictionary *p2c;
    IntrospectResponse *response;
} IntrospectState;

IntrospectResponse DominoSandbox::Introspect() const
{
    IntrospectResponse result
    {
        .numAttachedClients  = reportQueues_->getBucketCount(),
        .numTrackedProcesses = trackedProcesses_->getCount(),
        .numReportedPips     = 0,
        .pips                = {0}
    };
    
    OSDictionary *proc2children = OSDictionary::withCapacity(trackedProcesses_->getCount());
    if (!proc2children)
    {
        return result;
    }
    
    IntrospectState forEachState
    {
        .p2c = proc2children,
        .response = &result
    };
    
    // step 1: Create a PID -> PID[] dictionary mapping root PIDs to their child PIDs from the existing
    //         trackedProcesses_ dictionary which maps PID -> ProcessObject (i.e., tracked process to its root process).
    //
    //         Along the way, insert every newly encountered root process into 'result.rootProcesses'.
    trackedProcesses_->forEach(&forEachState, [](void *data, int idx, const OSSymbol *pidSym, const OSObject *value)
    {
        IntrospectState *state = (IntrospectState*)data;
        ProcessObject *proc = OSDynamicCast(ProcessObject, value);
        OSArray *children = OSDynamicCast(OSArray, state->p2c->getObject(proc->getHashCode()));
        if (children == nullptr)
        {
            OSArray *newArray = OSArray::withCapacity(10);
            children = newArray;
            if (newArray)
            {
                state->p2c->setObject(proc->getHashCode(), newArray);
                if (state->response->numReportedPips < kMaxReportedPips)
                {
                    state->response->pips[state->response->numReportedPips] = proc->Introspect();
                    state->response->numReportedPips++;
                }
            }
            OSSafeReleaseNULL(newArray);
        }
        OSNumber *pidNum = OSNumber::withNumber(pidSym->getCStringNoCopy(), 32);
        children->setObject(children->getCount(), pidNum);
        OSSafeReleaseNULL(pidNum);
    });

    // step 2: populate 'children' field for each root process in 'result.rootProcesses'
    for (int i = 0; i < result.numReportedPips; i++)
    {
        const OSSymbol *pidSym = ProcessObject::computePidHashCode(result.pips[i].pid);
        const OSArray *children = OSDynamicCast(OSArray, proc2children->getObject(pidSym));
        OSSafeReleaseNULL(pidSym);
        
        result.pips[i].numReportedChildren = min(kMaxReportedChildProcesses, children->getCount());
        for (int j = 0; j < result.pips[i].numReportedChildren; j++)
        {
            OSNumber *childPidNum = OSDynamicCast(OSNumber, children->getObject(j));
            pid_t childPid = childPidNum->unsigned32BitValue();
            result.pips[i].children[j].pid = childPid;
        }
    }

    OSSafeReleaseNULL(proc2children);
    return result;
}

#undef super
