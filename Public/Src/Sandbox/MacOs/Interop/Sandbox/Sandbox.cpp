// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "EventProcessor.hpp"
#include "IOHandler.hpp"
#include "Sandbox.hpp"

static Sandbox* sandbox;

extern "C"
{
#pragma mark Exported interop methods
    
    void InitializeSandbox(SandboxConnectionInfo *info, pid_t host_pid)
    {
        try
        {
            sandbox = new Sandbox(host_pid, info->config);
        }
        catch (BuildXLException ex)
        {
            log_error("Failed creating sandbox instance - %{public}s", ex.what());
            info->error = SB_INSTANCE_ERROR;
            return;
        }
    }
    
    void DeinitializeSandbox()
    {
        delete sandbox;
        log_debug("%s", "Successfully shut-down gerneric sandbox subsystem.");
    }

    __cdecl void ObserverFileAccessReports(SandboxConnectionInfo *info, AccessReportCallback callback, long accessReportSize)
    {
        if (sizeof(AccessReport) != accessReportSize)
        {
            log_error("Wrong size of the AccessReport buffer: expected %ld, received %ld!", sizeof(AccessReport), accessReportSize);
            if (callback != NULL) callback(AccessReport{}, SB_WRONG_BUFFER_SIZE);
            return;
        }

        if (callback == NULL)
        {
            log_error("%s", "No callback has been supplied for observation event reporting!");
            return;
        }

        sandbox->SetAccessReportCallback(callback);

        log_debug("Listening for observation reports for build host with pid (%d)...", getpid());
    }
}

#pragma mark Generic sandbox stubs

bool Sandbox_SendPipStarted(const pid_t pid, pipid_t pipId, const char *const famBytes, int famBytesLength)
{
    log_debug("Pip with PipId = %#llX, PID = %d launching", pipId, pid);
    
    try {
        std::shared_ptr<SandboxedPip> pip(new SandboxedPip(pid, famBytes, famBytesLength));
        return sandbox->TrackRootProcess(pip);
    }
    catch (BuildXLException ex)
    {
        log_error("Failed tracking root process, error: %{public}s", ex.what());
        return false;
    }
}

bool Sandbox_SendPipProcessTerminated(pipid_t pipId, pid_t pid)
{
    log_debug("Pip with PipId = %#llX, PID = %d terminated", pipId, pid);
    
    IOHandler handler = IOHandler(sandbox);
    if (handler.TryInitializeWithTrackedProcess(pid) && handler.GetPipId() == pipId)
    {
        log_debug("Killing process (%d)", pid);
        handler.HandleProcessUntracked(pid);
        kill(pid, SIGTERM);
    }

    return true;
}

#pragma mark Sandbox implementation

Sandbox::Sandbox(pid_t host_pid, Configuration config)
{
    hostPid_ = host_pid;
    configuration_ = config;
    
    if (!SetProcessPidPair(GetAllowlistedPidMap(), host_pid, getppid()))
    {
        throw BuildXLException("Could not allowlist build host process id!");
    }
    
    accessReportCallback_ = nullptr;
    
    trackedProcesses_ = Trie<SandboxedProcess>::createUintTrie();
    if (!trackedProcesses_)
    {
        throw BuildXLException("Could not create Trie for process tracking!");
    }
    
#if __APPLE__
    xpc_bridge_ = xpc_connection_create_mach_service("com.microsoft.buildxl.sandbox", NULL, 0);
    xpc_connection_set_event_handler(xpc_bridge_, ^(xpc_object_t message)
    {
        xpc_type_t type = xpc_get_type(message);
        if (type == XPC_TYPE_ERROR)
        {
            
        }
    });
    xpc_connection_activate(xpc_bridge_);
    
    hybird_event_queue_ = dispatch_queue_create("com.microsoft.buildxl.interop.hybrid_events", dispatch_queue_attr_make_with_qos_class(
        DISPATCH_QUEUE_SERIAL, QOS_CLASS_USER_INTERACTIVE, -1
    ));
#endif
    
    switch (configuration_)
    {
#if __APPLE__
        case EndpointSecuritySandboxType: {
            es_ = new EndpointSecuritySandbox(host_pid, &process_event, (void *)this, xpc_bridge_);
            break;
        }
        case DetoursSandboxType: {
            detours_ = new DetoursSandbox(host_pid, &process_event, (void *)this, xpc_bridge_);
            break;
        }
        case HybridSandboxType: {
            es_ = new EndpointSecuritySandbox(host_pid, &process_event, (void *)this, xpc_bridge_);
            detours_ = new DetoursSandbox(host_pid, &process_event, (void *)this, xpc_bridge_);
            break;
        }
#elif __linux__
        case DetoursLinuxSandboxType:
            break;
#endif
        default:
            throw BuildXLException("Could not infer sandbox configuration setting, aborting!");
    }
}

Sandbox::~Sandbox()
{
    accessReportCallback_ = nullptr;
    
    if (trackedProcesses_ != nullptr)
    {
        delete trackedProcesses_;
    }

#if __APPLE__
    if (es_ != nullptr)
    {
        delete es_;
    }
    
    if (detours_ != nullptr)
    {
        delete detours_;
    }
    
    xpc_connection_cancel(xpc_bridge_);
    xpc_release(xpc_bridge_);
    xpc_bridge_ = nullptr;
    
    if (hybird_event_queue_)
    {
        dispatch_release(hybird_event_queue_);
    }
#endif
}

std::shared_ptr<SandboxedProcess> Sandbox::FindTrackedProcess(pid_t pid)
{
    return trackedProcesses_->get(pid);
}

bool Sandbox::TrackRootProcess(std::shared_ptr<SandboxedPip> pip)
{
    pid_t pid = pip->GetProcessId();
    std::shared_ptr<SandboxedProcess> process(new SandboxedProcess(pid, pip));

    if (process == nullptr)
    {
        return false;
    }
    
    log_debug("Pip with PipId = %#llX, PID = %d launching", pip->GetPipId(), pid);
    
    int len = PATH_MAX;
    process->SetPath(pip->GetProcessPath(&len));
    
    int numAttempts = 0;
    while (++numAttempts <= 3)
    {
        TrieResult result = trackedProcesses_->insert(pid, process);
        if (result == TrieResult::kTrieResultAlreadyExists)
        {
            // if mapping for 'pid' exists (this can happen only if clients are nested) --> remove it and retry
            IOHandler handler = IOHandler(this);
            if (handler.TryInitializeWithTrackedProcess(pid))
            {
                handler.HandleProcessUntracked(pid);
                log_debug("EARLY untracking PID(%d); Previous :: RootPID: %d, PipId: %#llX, tree size: %d)",
                          pid, handler.GetProcessId(), handler.GetPipId(), handler.GetProcessTreeSize());
            }

            continue;
        }
        else
        {
            bool insertedNew = result == TrieResult::kTrieResultInserted;
            log_debug("Tracking root process PID(%d), PipId: %#llX, tree size: %d, path: %{public}s, code: %d",
                      pid, pip->GetPipId(), pip->GetTreeSize(), process->GetPath(), result);
            
            return insertedNew;
        }
    }
    
    process.reset();
    log_error("Exceeded max number of attempts in TrackRootProcess: %d - aborting!", numAttempts);
    return false;
}

bool Sandbox::TrackChildProcess(pid_t childPid, const char *childExecutable, std::shared_ptr<SandboxedProcess> parentProcess)
{
    std::shared_ptr<SandboxedPip> pip = parentProcess->GetPip();
    std::shared_ptr<SandboxedProcess> childProcess (new SandboxedProcess(childPid, pip));

    if (childProcess == nullptr)
    {
        return false;
    }

    TrieResult getOrAddResult;
    std::shared_ptr<SandboxedProcess> newValue = trackedProcesses_->getOrAdd(childPid, childProcess, &getOrAddResult);
    
    // Operation getOrAdd failed:
    //   -> skip everything and return error (should not happen under normal circumstances)
    if (newValue == nullptr)
    {
        goto error;
    }

    // There was already a process associated with this 'childPid':
    //   -> log an appropriate message and return false to indicate that no new process has been tracked
    if (getOrAddResult == TrieResult::kTrieResultAlreadyExists)
    {
        if (newValue->GetPip() == pip)
        {
            log_debug("Child process PID(%d) already tracked by the same Root PID(%d)", childPid, pip->GetProcessId());
        }
        else if (newValue->GetPip()->GetProcessId() == childPid)
        {
            log_debug("Child process PID(%d) cannot be added to Root PID(%d) because it has already been promoted to root itself",
                      childPid, pip->GetProcessId());
        }
        else
        {
            log_debug("Child process PID(%d) already tracked by a different Root PID(%d); intended new: Root PID(%d) (Code: %d)",
                      childPid, newValue->GetPip()->GetProcessId(), pip->GetProcessId(), getOrAddResult);
        }
        
        goto error;
    }

    // We associated 'process' with 'childPid' -> increment process tree and return true to indicate that a new process is being tracked
    if (getOrAddResult == TrieResult::kTrieResultInserted)
    {
        // copy the path from the parent process (because the child process always starts out as a fork of the parent)
        childProcess->SetPath(childExecutable);
        pip->IncrementProcessTreeCount();
        
        log_debug("Track entry %d -> %d, PipId: %#llX, New tree size: %d", childPid, pip->GetProcessId(), pip->GetPipId(), pip->GetTreeSize());
        
        return true;
    }

error:

    log_debug("Failed tracking child entry %d -> %d, PipId: %#llX, Tree size: %d, Code: %d",
              childPid, pip->GetProcessId(), pip->GetPipId(), pip->GetTreeSize(), getOrAddResult);
    
    childProcess.reset();
    return false;
}

bool Sandbox::UntrackProcess(pid_t pid, std::shared_ptr<SandboxedProcess> process)
{
    // remove the mapping for 'pid'
    auto removeResult = trackedProcesses_->remove(pid);
    bool removedExisting = removeResult == TrieResult::kTrieResultRemoved;
    if (removedExisting)
    {
        process->GetPip()->DecrementProcessTreeCount();
    }
    
    std::shared_ptr<SandboxedPip> pip = process->GetPip();
    
    log_debug("Untrack entry %d (%{public}s) -> %d, PipId: %#llX, New tree size: %d, Code: %d",
              pid, process->GetPath(), pip->GetProcessId(), pip->GetPipId(), pip->GetTreeSize(), removeResult);
    
    return removedExisting;
}

void const Sandbox::SendAccessReport(AccessReport &report, std::shared_ptr<SandboxedPip> pip)
{
    assert(strlen(report.path) > 0);
    accessReportCallback_(report, REPORT_QUEUE_SUCCESS);

    log_debug("Enqueued PID(%d), Root PID(%d), PIP(%#llX), Operation: %{public}s, Path: %{public}s, Status: %d",
              report.pid, report.rootPid, report.pipId, OpNames[report.operation], report.path, report.status);
}
