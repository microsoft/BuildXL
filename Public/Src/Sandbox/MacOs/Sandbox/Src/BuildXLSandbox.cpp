// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <kern/clock.h>

#include "BuildXLSandbox.hpp"
#include "CacheRecord.hpp"
#include "Listeners.hpp"
#include "Stopwatch.hpp"
#include "SysCtl.hpp"
#include "TrustedBsdHandler.hpp"

#define LogVerbose(format, ...) log_verbose(g_bxl_verbose_logging, format, __VA_ARGS__)
#define super IOService

OSDefineMetaClassAndStructors(BuildXLSandbox, IOService)

os_log_t logger = os_log_create(kBuildXLBundleIdentifier, "Logger");

static KextConfig sDefaultConfig =
{
    .reportQueueSizeMB    = kSharedDataQueueSizeDefault,
    .enableReportBatching = false,
    .enableCatalinaDataPartitionFiltering = false,
    .resourceThresholds   =
    {
        .cpuUsageBlock     = 0,
        .cpuUsageWakeup    = 0,
        .minAvailableRamMB = 0
    }
};

bool BuildXLSandbox::init(OSDictionary *dictionary)
{
    if (!super::init(dictionary))
    {
        return false;
    }

#if DEBUG
    gIOKitDebug |= kIOKitDebugUserOptions;
#endif

    bxl_sysctl_register();
    InitializePolicyStructures();

    lock_ = IORecursiveLockAlloc();
    if (!lock_)
    {
        return false;
    }

    ResetCounters();
    resourceManager_ = ResourceManager::create(&counters_.resourceCounters);
    if (!resourceManager_)
    {
        return false;
    }

    Configure(&sDefaultConfig);
    if (!InitializeTries())
    {
        return false;
    }

    return true;
}

void BuildXLSandbox::free(void)
{
    UninitializeListeners();

    if (lock_)
    {
        IORecursiveLockFree(lock_);
        lock_ = nullptr;
    }

    OSSafeReleaseNULL(resourceManager_);
    OSSafeReleaseNULL(trackedProcesses_);
    OSSafeReleaseNULL(connectedClients_);

    bxl_sysctl_unregister();

    super::free();
}

bool BuildXLSandbox::start(IOService *provider)
{
    bool success = super::start(provider);
    if (success)
    {
        registerService();
    }

    return success;
}

void BuildXLSandbox::stop(IOService *provider)
{
    super::stop(provider);
}

void BuildXLSandbox::InitializePolicyStructures()
{
    Listeners::g_dispatcher = this;

    buildxlPolicyOps_ =
    {
        // NOTE: handle preflight instead of mpo_vnode_check_lookup because trying to get the path for a vnode
        //       (vn_getpath) inside of that handler overwhelms the system very quickly
        .mpo_vnode_check_lookup_preflight = Listeners::mpo_vnode_check_lookup_pre,

        // this event happens on the parent process before it forks
        .mpo_proc_check_fork              = Listeners::mpo_proc_check_fork,

        // this event happens right after fork only on the child processes
        .mpo_cred_label_associate_fork    = Listeners::mpo_cred_label_associate_fork,

        // some tools spawn child processes using execve() and vfork(), while this is non standard, we have to handle it
        // especially for shells like csh / tcsh
        .mpo_cred_label_update_execve     = Listeners::mpo_cred_label_update_execve,

        .mpo_vnode_check_exec             = Listeners::mpo_vnode_check_exec,

        .mpo_proc_notify_exit             = Listeners::mpo_proc_notify_exit,

        .mpo_vnode_check_create           = Listeners::mpo_vnode_check_create,

        .mpo_vnode_check_write            = Listeners::mpo_vnode_check_write,

        .mpo_vnode_check_readlink         = Listeners::mpo_vnode_check_readlink,

        .mpo_vnode_check_clone            = Listeners::mpo_vnode_check_clone,
    };

    policyConfiguration_ =
    {
        .mpc_name            = kBuildXLSandboxClassName,
        .mpc_fullname        = "Sandbox for process liftetime, I/O observation and control",
        .mpc_labelnames      = NULL,
        .mpc_labelname_count = 0,
        .mpc_ops             = &buildxlPolicyOps_,
        .mpc_loadtime_flags  = MPC_LOADTIME_FLAG_UNLOADOK,
        .mpc_field_off       = NULL,
        .mpc_runtime_flags   = 0,
        .mpc_list            = NULL,
        .mpc_data            = NULL
    };
}

bool BuildXLSandbox::InitializeTries()
{
    connectedClients_ = Trie::createUintTrie();
    if (!connectedClients_)
    {
        return false;
    }

    trackedProcesses_ = Trie::createUintTrie();
    if (!trackedProcesses_)
    {
        return false;
    }

    bool callbackInstalled = trackedProcesses_->onChange(this, [](void *data, int oldCount, int newCount)
    {
        BuildXLSandbox *me = (BuildXLSandbox*)data;
        if (me->resourceManager_ != nullptr)
        {
            me->resourceManager_->UpdateNumTrackedProcesses(newCount);
        }
    });

    if (!callbackInstalled)
    {
        log_error("%s", "Could not install callback for tracked processes");
        return false;
    }

    // Install an 'onChange' callback, which uninitializes (initializes) listeners
    // whenever the number of attached clients drops to (moves from) 0.
    callbackInstalled = connectedClients_->onChange(this, [](void *data, int oldCount, int newCount)
    {
        BuildXLSandbox *me = (BuildXLSandbox*)data;
        if (newCount == 0)
        {
            LogVerbose("Number of attached clients dropped from %d to 0 --> uninitializing listeners", oldCount);

            // Unregistering listeners on a separate thread, because doing it on a crashed user level thread
            // causes a dealock inside of IOService
            thread_t once;
            kernel_thread_start([](void *args, int)
                                {
                                    ((BuildXLSandbox*)args)->OnLastClientDisconnected();
                                    thread_terminate(current_thread());
                                }, me, &once);
            thread_deallocate(once);
        }
        else if (oldCount == 0)
        {
            LogVerbose("Number of attached clients jumped from 0 to %d --> initializing listeners", newCount);
            me->InitializeListeners();
        }
    });

    if (!callbackInstalled)
    {
        log_error("%s", "Could not install callback for reacting to when number of attached clients changes");
        return false;
    }

    return true;
}

kern_return_t BuildXLSandbox::InitializeListeners()
{
    policyHandle_ = 0;
    kern_return_t status = mac_policy_register(&policyConfiguration_, &policyHandle_, NULL);
    if (status != KERN_SUCCESS)
    {
        log_error("Registering TrustedBSD MAC policy callbacks failed with error code %#X", status);
        return status;
    }

#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated"
    buildxlVnodeListener_ = kauth_listen_scope(KAUTH_SCOPE_VNODE, Listeners::buildxl_vnode_listener, reinterpret_cast<void *>(this));
    if (buildxlVnodeListener_ == nullptr)
    {
        log_error("%s", "Registering callback for KAUTH_SCOPE_VNODE scope failed!");
        return KERN_FAILURE;
    }

    buildxlFileOpListener_ = kauth_listen_scope(KAUTH_SCOPE_FILEOP, Listeners::buildxl_file_op_listener, reinterpret_cast<void *>(this));
    if (buildxlFileOpListener_ == nullptr)
    {
        log_error("%s", "Registering callback for KAUTH_SCOPE_FILEOP scope failed!");
        return KERN_FAILURE;
    }
#pragma clang diagnostic pop

    LogVerbose("%s", "Successfully registered listeners");
    return KERN_SUCCESS;
}

void BuildXLSandbox::UninitializeListeners()
{
    counters_ = {0};

#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated"
    if (buildxlVnodeListener_ != nullptr)
    {
        kauth_unlisten_scope(buildxlVnodeListener_);
        LogVerbose("%s", "Deregistered callback for KAUTH_SCOPE_VNODE scope");
        buildxlVnodeListener_ = nullptr;
    }

    if (buildxlFileOpListener_ != nullptr)
    {
        kauth_unlisten_scope(buildxlFileOpListener_);
        LogVerbose("%s", "Deregistered callback for KAUTH_SCOPE_FILEOP scope");
        buildxlFileOpListener_ = nullptr;
    }
#pragma clang diagnostic pop
    
    if (policyHandle_ != 0)
    {
        mac_policy_unregister(policyHandle_);
        LogVerbose("%s", "Deregistered TrustedBSD MAC policy callbacks");
        policyHandle_ = 0;
    }
}

void BuildXLSandbox::OnLastClientDisconnected()
{
    EnterMonitor

    Configure(&sDefaultConfig);
    ResetCounters();
    UninitializeListeners();

    // re-initialize tries to force deallocation of trie nodes
    OSSafeReleaseNULL(trackedProcesses_);
    OSSafeReleaseNULL(connectedClients_);
    InitializeTries();
}

void BuildXLSandbox::Configure(const KextConfig *config)
{
    EnterMonitor

    config_ = *config;

    if (resourceManager_)
    {
        resourceManager_->SetThresholds(config_.resourceThresholds);
    }

    // validate
    if (config_.reportQueueSizeMB == 0 || config_.reportQueueSizeMB > kSharedDataQueueSizeMax)
    {
        config_.reportQueueSizeMB = kSharedDataQueueSizeDefault;
    }
}

UInt32 BuildXLSandbox::GetReportQueueEntryCount()
{
    return (config_.reportQueueSizeMB * 1024 * 1024) / sizeof(AccessReport);
}

ClientInfo* BuildXLSandbox::GetClientInfo(pid_t clientPid)
{
    return connectedClients_->getAs<ClientInfo>(clientPid);
}

IOReturn BuildXLSandbox::AllocateNewClient(pid_t clientPid)
{
    EnterMonitor

    ClientInfo *client = ClientInfo::create(
    {
        .entryCount     = GetReportQueueEntryCount(),
        .entrySize      = sizeof(AccessReport),
        .enableBatching = config_.enableReportBatching,
        .counters       = &counters_.reportCounters
    });
    AutoRelease _(client);

    if (client == nullptr)
    {
        log_error("Couldn't allocate a new client with PID(%d)", clientPid);
        return kIOReturnError;
    }

    auto insertResult = connectedClients_->insert(clientPid, client);
    if (insertResult == Trie::TrieResult::kTrieResultInserted)
    {
        return kIOReturnSuccess;
    }
    else
    {
        log_error("Couldn't insert a client with PID(%d). Error code: %d", clientPid, insertResult);
        return kIOReturnError;
    }
}

IOReturn BuildXLSandbox::DeallocateClient(pid_t clientPid)
{
    EnterMonitor

    auto removeResult = connectedClients_->remove(clientPid);

    if (removeResult == Trie::TrieResult::kTrieResultFailure ||
        removeResult == Trie::TrieResult::kTrieResultRace)
    {
        log_error("Deallocating client PID(%d) failed with code %d", clientPid, removeResult);
        return kIOReturnError;
    }

    if (removeResult == Trie::TrieResult::kTrieResultAlreadyEmpty)
    {
        // we are not tracking this client (e.g., the client was SandboxMonitor)
        log_debug("Client PID(%d) not tracked", clientPid);
        return kIOReturnSuccess;
    }

    if (removeResult == Trie::TrieResult::kTrieResultRemoved)
    {
        log_debug("Deallocating client PID(%d)", clientPid);

        // Make sure to also cleanup any remaining tracked process objects as the client could have exited abnormally (crashed)
        // and we don't want those objects to stay around any longer
        trackedProcesses_->removeMatching(&clientPid, [](void *data, const OSObject *value)
        {
            pid_t cid = *static_cast<pid_t*>(data);
            SandboxedProcess *process = OSDynamicCast(SandboxedProcess, value);
            return process != nullptr && process->getPip()->getClientPid() == cid;
        });

        return kIOReturnSuccess;
    }

    log_error("Unknown remove result: %d", removeResult);
    return kIOReturnError;
}

IOReturn BuildXLSandbox::SetReportQueueNotificationPort(mach_port_t port, pid_t clientPid)
{
    EnterMonitor

    ClientInfo *client = GetClientInfo(clientPid);
    bool success =
        client != nullptr &&
        client->setNotifactonPort(port);

    return success ? kIOReturnSuccess : kIOReturnError;
}

IOMemoryDescriptor* const BuildXLSandbox::GetReportQueueMemoryDescriptor(pid_t clientPid)
{
    EnterMonitor

    ClientInfo *client = GetClientInfo(clientPid);
    return client != nullptr
        ? client->getMemoryDescriptor()
        : nullptr;
}

bool const BuildXLSandbox::SendAccessReport(AccessReport &report, SandboxedPip *pip, const CacheRecord *cacheRecord)
{
    Stopwatch stopwatch;

    pid_t clientPid = pip->getClientPid();
    ClientInfo *client = GetClientInfo(clientPid);

    Timespan getClientInfoDuration      = stopwatch.lap();
    Counters()->getClientInfo          += getClientInfoDuration;
    pip->Counters()->getClientInfo += getClientInfoDuration;

    if (client == nullptr)
    {
        log_error("No client info found for PID(%d)", clientPid);
        return false;
    }

    AddTimeStampToAccessReport(&report, enqueueTime);

    bool success = client->enqueueReport({.report = report, .cacheRecord = cacheRecord});

    Timespan reportFileAccessDuration  = stopwatch.lap();
    Counters()->reportFileAccess      += reportFileAccessDuration;
    pip->Counters()->reportFileAccess += reportFileAccessDuration;

    log_error_or_debug(
        g_bxl_verbose_logging, !success,
        "Enqueued ClientPID(%d), PID(%d), Root PID(%d), PIP(%#llX), Operation: %s, Path: %s, Status: %d, Sent: %d",
        clientPid, report.pid, report.rootPid, report.pipId, OpNames[report.operation], report.path, report.status, success);

    return success;
}

SandboxedProcess* BuildXLSandbox::FindTrackedProcess(pid_t pid)
{
    // NOTE: this has to be very fast when we are not tracking any processes (i.e., trackedProcesses_ is empty)
    //       because this is called on every single file access any process makes
    return trackedProcesses_->getAs<SandboxedProcess>(pid);
}

bool BuildXLSandbox::TrackRootProcess(SandboxedPip *pip)
{
    pid_t pid = pip->getProcessId();

    SandboxedProcess *process = SandboxedProcess::create(pid, pip);
    AutoRelease _(process);

    if (process == nullptr)
    {
        return false;
    }

    int len = MAXPATHLEN;
    process->setPath(pip->getProcessPath(&len), len);

    int numAttempts = 0;
    while (++numAttempts <= 3)
    {
        auto result = trackedProcesses_->insert(pid, process);

        if (result == Trie::TrieResult::kTrieResultAlreadyExists)
        {
            // if mapping for 'pid' exists (this can happen only if clients are nested) --> remove it and retry
            TrustedBsdHandler handler = TrustedBsdHandler(this);
            if (handler.TryInitializeWithTrackedProcess(pid))
            {
                LogVerbose("EARLY untracking PID(%d) of ClientId(%d); Previous :: RootPID: %d, PipId: %#llX, tree size: %d)",
                           pid, handler.GetClientPid(), handler.GetProcessId(), handler.GetPipId(), handler.GetProcessTreeSize());
                handler.HandleProcessUntracked(pid); // consider: handler.HandleProcessExit(pid);
            }

            continue;
        }
        else
        {
            bool insertedNew = result == Trie::TrieResult::kTrieResultInserted;
            log_error_or_debug(g_bxl_verbose_logging,
                               !insertedNew,
                               "Tracking root process PID(%d) for ClientId(%d), PipId: %#llX, tree size: %d, path: %s, code: %d",
                               pid, pip->getClientPid(), pip->getPipId(), pip->getTreeSize(), process->getPath	(), result);
            return insertedNew;
        }
    }

    log_error("Exceeded max number of attempts: %d", numAttempts);
    return false;
}

static OSObject* ProcessFactory(void *data)
{
    // It is IMPORTANT to retain 'process' here.  This function is used as a factory for Trie::getOrAdd which
    // expects it to increase the ref count of the object it returns (as a constructor would).
    // The 'getOrAdd' method immediatelly releases this object if it ends up creating it but not using it;
    // if it does end up using it, it releases it upon its removal from the trie.
    SandboxedProcess *process = (SandboxedProcess*)data;
    process->retain();
    return process;
}

bool BuildXLSandbox::TrackChildProcess(pid_t childPid, SandboxedProcess *parentProcess)
{
    SandboxedPip *pip = parentProcess->getPip();

    SandboxedProcess *childProcess = SandboxedProcess::create(childPid, pip);
    AutoRelease _(childProcess);

    if (childProcess == nullptr)
    {
        return false;
    }

    Trie::TrieResult getOrAddResult;
    OSObject *newValue = trackedProcesses_->getOrAdd(childPid, childProcess, ProcessFactory, &getOrAddResult);
    SandboxedProcess *existingProcess = OSDynamicCast(SandboxedProcess, newValue);

    // Operation getOrAdd failed:
    //   -> skip everything and return error (should not happen under normal circumstances)
    if (existingProcess == nullptr)
    {
        goto error;
    }

    // There was already a process associated with this 'childPid':
    //   -> log an appropriate message and return false to indicate that no new process has been tracked
    if (getOrAddResult == Trie::TrieResult::kTrieResultAlreadyExists)
    {
        if (existingProcess->getPip() == pip)
        {
            LogVerbose("Child process PID(%d) already tracked by the same Root PID(%d) for ClientId(%d)",
                       childPid, pip->getProcessId(), pip->getClientPid());
        }
        else if (existingProcess->getPip()->getProcessId() == childPid)
        {
            LogVerbose("Child process PID(%d) cannot be added to Root PID(%d) for ClientId(%d) "
                       "because it has already been promoted to root itself",
                       childPid, pip->getProcessId(), pip->getClientPid());
        }
        else
        {
            log_error("Child process PID(%d) already tracked by a different Root PID(%d)/ClientId(%d); "
                      "intended new: Root PID(%d)/ClientId(%d) (Code: %d)",
                      childPid, existingProcess->getPip()->getProcessId(), existingProcess->getPip()->getClientPid(),
                      pip->getProcessId(), pip->getClientPid(), getOrAddResult);
        }
        return false;
    }

    // We associated 'process' with 'childPid':
    //   -> increment process tree and return true to indicate that a new process is being tracked
    if (getOrAddResult == Trie::TrieResult::kTrieResultInserted)
    {
        // copy the path from the parent process (because the child process always starts out as a fork of the parent)
        childProcess->setPath(parentProcess->getPath());
        pip->incrementProcessTreeCount();
        LogVerbose("Track entry %d -> %d :: ClientId: %d, PipId: %#llX, New tree size: %d",
                   childPid, pip->getProcessId(), pip->getClientPid(),
                   pip->getPipId(), pip->getTreeSize());
        return true;
    }

error:

    log_error("Track entry %d -> %d FAILED :: ClientId: %d, PipId: %#llX, Tree size: %d, Code: %d",
              childPid, pip->getProcessId(), pip->getClientPid(),
              pip->getPipId(), pip->getTreeSize(), getOrAddResult);
    return false;
}

bool BuildXLSandbox::UntrackProcess(pid_t pid, SandboxedProcess *process)
{
    // remove the mapping for 'pid'
    auto removeResult = trackedProcesses_->remove(pid);
    bool removedExisting = removeResult == Trie::TrieResult::kTrieResultRemoved;
    if (removedExisting)
    {
        process->getPip()->decrementProcessTreeCount();
    }
    SandboxedPip *pip = process->getPip();
    log_error_or_debug(g_bxl_verbose_logging,
                       !removedExisting,
                       "Untrack entry %d -> %d :: ClientId: %d, PipId: %#llX, New tree size: %d, Code: %d",
                       pid, pip->getProcessId(), pip->getClientPid(),
                       pip->getPipId(), pip->getTreeSize(), removeResult);
    return removedExisting;
}

typedef struct {
    Trie *p2c;
    IntrospectResponse *response;
} IntrospectState;

static int BytesInAMegabyte = 1024 * 1024;

IntrospectResponse BuildXLSandbox::Introspect() const
{
    EnterMonitor

    IntrospectResponse result
    {
        .numAttachedClients  = connectedClients_->getCount(),
        .counters            = counters_,
        .kextConfig          = config_,
        .numReportedPips     = 0,
        .pips                = {0}
    };

    Trie::getUintNodeCounts(&result.counters.numUintTrieNodes, &result.counters.uintTrieSizeMB);
    Trie::getPathNodeCounts(&result.counters.numPathTrieNodes, &result.counters.pathTrieSizeMB);

    ReportCounters *reportCounters = &result.counters.reportCounters;
    reportCounters->freeListSizeMB =
        (sizeof(ConcurrentSharedDataQueue::ElemPayload)) * reportCounters->freeListNodeCount.count() * 1.0 / BytesInAMegabyte;

    Trie *proc2children = Trie::createUintTrie();
    AutoRelease _(proc2children);

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
    //         Along the way, insert every newly encountered root process into 'response.pips'.
    trackedProcesses_->forEach(&forEachState, [](void *data, uint64_t key, const OSObject *value)
    {
        IntrospectState *state = (IntrospectState*)data;
        SandboxedProcess *proc = OSDynamicCast(SandboxedProcess, value);
        if (proc == nullptr)
        {
            return;
        }

        proc->retain();
        AutoRelease _(proc);

        pid_t rootPID = proc->getPip()->getProcessId();
        OSArray *children = state->p2c->getAs<OSArray>(rootPID);
        if (children == nullptr)
        {
            OSArray *newArray = OSArray::withCapacity(10);
            AutoRelease _(newArray);

            children = newArray;
            if (newArray)
            {
                auto insertResult = state->p2c->insert(rootPID, newArray);
                if (insertResult != Trie::TrieResult::kTrieResultInserted)
                {
                    log_error("Could not insert PID %d, code: %d", rootPID, insertResult);
                }
                else if (state->response->numReportedPips < kMaxReportedPips)
                {
                    state->response->pips[state->response->numReportedPips] = proc->getPip()->introspect();
                    state->response->numReportedPips++;
                }
            }
        }
        OSNumber *pidNum = OSNumber::withNumber(key, 32);
        children->setObject(key == rootPID ? 0 : children->getCount(), pidNum);
        OSSafeReleaseNULL(pidNum);
    });

    // step 2: populate 'children' field for each root process in 'result.rootProcesses'
    for (int i = 0; i < result.numReportedPips; i++)
    {
        const OSArray *children = proc2children->getAs<OSArray>(result.pips[i].pid);

        result.pips[i].numReportedChildren = min(kMaxReportedChildProcesses, children->getCount());
        for (int j = 0; j < result.pips[i].numReportedChildren; j++)
        {
            OSNumber *childPidNum = OSDynamicCast(OSNumber, children->getObject(j));
            pid_t childPid = childPidNum->unsigned32BitValue();
            result.pips[i].children[j].pid = childPid;
        }
    }

    return result;
}

#undef super
