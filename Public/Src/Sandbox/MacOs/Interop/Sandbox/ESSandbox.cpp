// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifdef ES_SANDBOX

#include <signal.h>
#include "ESSandbox.h"
#include "BuildXLSandboxShared.hpp"

#include "IOHandler.hpp"

// initialized below in InitializeEndpointSecuritySandbox (which is called once by the host process)
static ESSandbox *sandbox;

void processEndpointSecurityEvent(es_client_t *client, const es_message_t *msg, pid_t host)
{
    pid_t pid = audit_token_to_pid(msg->process->audit_token);
    
    // Mute all events comming from BuildXL itself
    if (pid == host)
    {
        es_mute_process(client, &msg->process->audit_token);
        return;
    }
    
    IOHandler handler = IOHandler(sandbox);
    
    if (handler.TryInitializeWithTrackedProcess(pid))
    {
        switch (msg->event_type)
        {
            case ES_EVENT_TYPE_NOTIFY_EXEC:
                return handler.HandleProcessExec(msg);
                
            case ES_EVENT_TYPE_NOTIFY_FORK:
                return handler.HandleProcessFork(msg);
                
            case ES_EVENT_TYPE_NOTIFY_EXIT:
                return handler.HandleProcessExit(msg);
                
            case ES_EVENT_TYPE_NOTIFY_LOOKUP:
                return handler.HandleLookup(msg);
                
            case ES_EVENT_TYPE_NOTIFY_OPEN:
                return handler.HandleOpen(msg);
                
            case ES_EVENT_TYPE_NOTIFY_CLOSE:
                return handler.HandleClose(msg);

            case ES_EVENT_TYPE_NOTIFY_CREATE:
                return handler.HandleCreate(msg);

            // TODO: Decide what to do, tools touch source files too
            case ES_EVENT_TYPE_NOTIFY_SETATTRLIST:
            case ES_EVENT_TYPE_NOTIFY_SETEXTATTR:
            case ES_EVENT_TYPE_NOTIFY_SETFLAGS:
            case ES_EVENT_TYPE_NOTIFY_SETMODE:
            case ES_EVENT_TYPE_NOTIFY_WRITE:
                return handler.HandleWrite(msg);

            case ES_EVENT_TYPE_NOTIFY_EXCHANGEDATA:
                return handler.HandleExchange(msg);

            case ES_EVENT_TYPE_NOTIFY_RENAME:
                return handler.HandleRename(msg);
                
            case ES_EVENT_TYPE_NOTIFY_READLINK:
                return handler.HandleReadlink(msg);
                
            case ES_EVENT_TYPE_NOTIFY_LINK:
                return handler.HandleLink(msg);
                    
            case ES_EVENT_TYPE_NOTIFY_UNLINK:
                return handler.HandleUnlink(msg);
        }
    }
}

extern "C"
{
#pragma mark Exported interop methods

    void InitializeEndpointSecuritySandbox(ESConnectionInfo *info, pid_t host)
    {
        sandbox = new ESSandbox(^(es_client_t *client, const es_message_t *msg)
        {
            processEndpointSecurityEvent(client, msg, host);
        });
        
        es_client_t *client;
        es_new_client_result_t result = es_new_client(&client, sandbox->GetObservationHandler());
        if (result != ES_NEW_CLIENT_RESULT_SUCCESS)
        {
            log_error("Failed creating EndpointSecurity client with error code: (%d)\n", result);
            info->error = ES_CLIENT_CREATION_FAILED;
            return;
        }
        
        sandbox->SetESClient(client);
        
        es_clear_cache_result_t clearResult = es_clear_cache(client);
        if (clearResult != ES_CLEAR_CACHE_RESULT_SUCCESS)
        {
            log_error("%s", "Failed resetting result cache on EndpointSecurity client initialization!\n");
            info->error = ES_CLIENT_CACHE_RESET_FAILED;
            return;
        }
    
        info->client = (uintptr_t) client;
        info->source = (uintptr_t) CFRunLoopSourceCreate(NULL, 0, sandbox->GetRunLoopSourceContext());
    }
    
    void DeinitializeEndpointSecuritySandbox(ESConnectionInfo info)
    {
        es_client_t *client = (es_client_t *) info.client;
        
        es_return_t result = es_unsubscribe_all(client);
        if (result != ES_RETURN_SUCCESS)
        {
            log_error("%s", "Failed unsubscribing from all EndpointSecurity events on client tear-down!\n");
        }
        
        result = es_delete_client(client);
        if (result != ES_RETURN_SUCCESS)
        {
            log_error("%s", "Failed deleting the EndpointSecurity client!\n");
        }
        
        CFRunLoopRef runLoop = (CFRunLoopRef) info.runLoop;
        CFRunLoopSourceRef source = (CFRunLoopSourceRef) info.source;
        
        CFRunLoopRemoveSource(runLoop, source, kCFRunLoopDefaultMode);
        CFRunLoopSourceInvalidate(source);
        CFRelease(source);
        
        CFRunLoopStop(runLoop);
        delete sandbox;
        log_debug("%s", "Successfully shut down EndpointSecurity subystem...");
    }

    __cdecl void ObserverFileAccessReports(ESConnectionInfo *info, AccessReportCallback callback, long accessReportSize)
    {
        if (sizeof(AccessReport) != accessReportSize)
        {
            log_error("Wrong size of the AccessReport buffer: expected %ld, received %ld", sizeof(AccessReport), accessReportSize);
            if (callback != NULL) callback(AccessReport{}, ES_WRONG_BUFFER_SIZE);
            return;
        }

        if (callback == NULL)
        {
            log_error("%s", "No callback has been supplied for EndpointSecurity file observation!");
            return;
        }
        
        sandbox->SetAccessReportCallback(callback);
        es_client_t *client = (es_client_t *) info->client;
        
        // Subsribe and activate the ES client
        
        es_return_t status = es_subscribe(client, sandbox->GetSubscibedESEvents(), sandbox->GetSubscribedESEventsCount());
        if (status != ES_RETURN_SUCCESS)
        {
            log_error("%s", "Failed subscribing to EndpointSecurity events, please check the sandbox configuration!");
            if (callback != NULL) callback(AccessReport{}, ES_CLIENT_SUBSCRIBE_FAILED);
            return;
        }
        
        log_debug("Listening for reports of the EndpointSecurity sub system from process: %d", getpid());
        
        info->runLoop = (uintptr_t) CFRunLoopGetCurrent();
        
        // Use a dedicated run-loop for the thread so we can continously observe ES events
        CFRunLoopAddSource((CFRunLoopRef) info->runLoop, (CFRunLoopSourceRef) info->source, kCFRunLoopDefaultMode);
        CFRunLoopRun();
    }
}

#pragma mark EndpointSecurity stubs

bool ES_SendPipStarted(const pid_t pid, pipid_t pipId, const char *const famBytes, int famBytesLength)
{
    log_debug("Pip with PipId = %#llX, PID = %d launching", pipId, pid);
    SandboxedPip *entry = new SandboxedPip(pid, famBytes, famBytesLength);
    return sandbox->TrackRootProcess(entry);
}

bool ES_SendPipProcessTerminated(pipid_t pipId, pid_t pid)
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

#pragma mark ESSandbox implementation

ESSandbox::~ESSandbox()
{
    Block_release(esObservationHandler_);
    esObservationHandler_ = nullptr;
    client = nullptr;
    
    if (trackedProcesses_ != nullptr)
    {
        delete trackedProcesses_;
    }
}

SandboxedProcess* ESSandbox::FindTrackedProcess(pid_t pid)
{
    // NOTE: this has to be very fast when we are not tracking any processes (i.e., trackedProcesses_ is empty)
    //       because this is called on every single file access any process makes
    return trackedProcesses_->get(pid);
}

bool ESSandbox::TrackRootProcess(SandboxedPip *pip)
{
    pid_t pid = pip->getProcessId();
    SandboxedProcess *process = new SandboxedProcess(pid, pip);

    if (process == nullptr)
    {
        return false;
    }

    int len = MAXPATHLEN;
    process->setPath(pip->getProcessPath(&len), len);
    
    int numAttempts = 0;
    while (++numAttempts <= 3)
    {
        Trie::TrieResult result = trackedProcesses_->insert(pid, process);
        if (result == Trie::TrieResult::kTrieResultAlreadyExists)
        {
            // if mapping for 'pid' exists (this can happen only if clients are nested) --> remove it and retry
            IOHandler handler = IOHandler(this);
            if (handler.TryInitializeWithTrackedProcess(pid))
            {
                log_debug("EARLY untracking PID(%d); Previous :: RootPID: %d, PipId: %#llX, tree size: %d)",
                          pid, handler.GetProcessId(), handler.GetPipId(), handler.GetProcessTreeSize());
                          handler.HandleProcessUntracked(pid); // consider: handler.HandleProcessExit(pid);
            }

            continue;
        }
        else
        {
            bool insertedNew = result == Trie::TrieResult::kTrieResultInserted;
            log_debug("Tracking root process PID(%d), PipId: %#llX, tree size: %d, path: %{public}s, code: %d",
                      pid, pip->getPipId(), pip->getTreeSize(), process->getPath(), result);
            
            return insertedNew;
        }
    }

    log_debug("Exceeded max number of attempts: %d", numAttempts);
    return false;
}

bool ESSandbox::TrackChildProcess(pid_t childPid, SandboxedProcess *parentProcess)
{
    SandboxedPip *pip = parentProcess->getPip();
    SandboxedProcess *childProcess = new SandboxedProcess(childPid, pip);

    if (childProcess == nullptr)
    {
        return false;
    }

    Trie::TrieResult getOrAddResult;
    SandboxedProcess *newValue = trackedProcesses_->getOrAdd(childPid, childProcess, &getOrAddResult);
    
    // Operation getOrAdd failed:
    //   -> skip everything and return error (should not happen under normal circumstances)
    if (newValue == nullptr)
    {
        goto error;
    }

    // There was already a process associated with this 'childPid':
    //   -> log an appropriate message and return false to indicate that no new process has been tracked
    if (getOrAddResult == Trie::TrieResult::kTrieResultAlreadyExists)
    {
        if (newValue->getPip() == pip)
        {
            log_debug("Child process PID(%d) already tracked by the same Root PID(%d)", childPid, pip->getProcessId());
        }
        else if (newValue->getPip()->getProcessId() == childPid)
        {
            log_debug("Child process PID(%d) cannot be added to Root PID(%d) because it has already been promoted to root itself",
                      childPid, pip->getProcessId());
        }
        else
        {
            log_debug("Child process PID(%d) already tracked by a different Root PID(%d); intended new: Root PID(%d) (Code: %d)",
                      childPid, newValue->getPip()->getProcessId(), pip->getProcessId(), getOrAddResult);
        }
        return false;
    }

    // We associated 'process' with 'childPid' -> increment process tree and return true to indicate that a new process is being tracked
    if (getOrAddResult == Trie::TrieResult::kTrieResultInserted)
    {
        // copy the path from the parent process (because the child process always starts out as a fork of the parent)
        childProcess->setPath(parentProcess->getPath());
        pip->incrementProcessTreeCount();
        
        log_debug("Track entry %d -> %d, PipId: %#llX, New tree size: %d", childPid, pip->getProcessId(), pip->getPipId(), pip->getTreeSize());
        
        return true;
    }

error:

    log_debug("Track entry %d -> %d FAILED, PipId: %#llX, Tree size: %d, Code: %d",
              childPid, pip->getProcessId(), pip->getPipId(), pip->getTreeSize(), getOrAddResult);
    
    return false;
}

bool ESSandbox::UntrackProcess(pid_t pid, SandboxedProcess *process)
{
    // remove the mapping for 'pid'
    auto removeResult = trackedProcesses_->remove(pid);
    bool removedExisting = removeResult == Trie::TrieResult::kTrieResultRemoved;
    if (removedExisting)
    {
        process->getPip()->decrementProcessTreeCount();
    }
    
    SandboxedPip *pip = process->getPip();
    
    log_debug("Untrack entry %d (%{public}s) -> %d, PipId: %#llX, New tree size: %d, Code: %d",
              pid, process->getPathBuffer(), pip->getProcessId(), pip->getPipId(), pip->getTreeSize(), removeResult);
    
    return removedExisting;
}

void const ESSandbox::SendAccessReport(AccessReport &report, SandboxedPip *pip)
{
    report.stats.enqueueTime = mach_absolute_time();

    this->GetAccessReportCallback()(report, REPORT_QUEUE_SUCCESS);

    log_debug("Enqueued PID(%d), Root PID(%d), PIP(%#llX), Operation: %{public}s, Path: %{public}s, Status: %d",
              report.pid, report.rootPid, report.pipId, OpNames[report.operation], report.path, report.status);
}

#endif /* ES_SANDBOX */
