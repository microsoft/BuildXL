// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef EventProcessor_h
#define EventProcessor_h

#include "IOEvent.hpp"
#include "IOHandler.hpp"
#include "Sandbox.hpp"

static ProcessCallbackResult _process_event(Sandbox *sandbox, const IOEvent &event, pid_t host, IOEventBacking backing)
{
    pid_t pid = event.GetPid();
    if (pid == host)
    {
        return ProcessCallbackResult::Done;
    }
    
    bool isInterposedEvent = backing == IOEventBacking::Interposing;
    
    bool ppid_found = sandbox->GetAllowlistedPidMap().find(event.GetParentPid()) != sandbox->GetAllowlistedPidMap().end();
    bool original_ppid_found = sandbox->GetAllowlistedPidMap().find(event.GetOriginalParentPid()) != sandbox->GetAllowlistedPidMap().end();
    
    if (isInterposedEvent || (ppid_found || original_ppid_found))
    {
        IOHandler handler = IOHandler(sandbox);
        
        if (isInterposedEvent)
        {
            // Some Apple tools use posix_spawn* family functions to execute other binaries - those binaries sometimes do
            // synchronous operations, blocking the caller until their execution finishes. This leads to fork events being reported
            // after all other I/O events when interposing within the new binary. Because there is no way to get the child pid before
            // the posix_spawn* call returns, we have to manually add a fork event here if the parent of the binary in question is
            // already being tracked.
            
            if (event.GetEventType() == ES_EVENT_TYPE_NOTIFY_FORK && !sandbox->GetForceForkedPidMap().empty())
            {
                auto it = sandbox->GetForceForkedPidMap().find(event.GetChildPid());
                if (it != sandbox->GetForceForkedPidMap().end())
                {
                    if (it->first == event.GetChildPid() && it->second == event.GetPid())
                    {
                        sandbox->RemoveProcessPid(sandbox->GetForceForkedPidMap(), event.GetChildPid());
                        
                        log_debug("Ignoring fork event, previously forced fork for child PID(%d) and PPID(%d) with path: %{public}s",
                                  event.GetChildPid(), event.GetPid(), event.GetExecutablePath());
                        
                        return ProcessCallbackResult::Done;
                    }
                }
            }
            
            bool isTracked = handler.TryInitializeWithTrackedProcess(pid);
            if (!isTracked && (event.GetEventType() != ES_EVENT_TYPE_NOTIFY_EXEC && event.GetEventType() != ES_EVENT_TYPE_NOTIFY_EXIT))
            {
                IOEvent fork_event(event.GetParentPid(), event.GetPid(), event.GetParentPid(), ES_EVENT_TYPE_NOTIFY_FORK, "", "", event.GetExecutablePath(), false);
                
                IOHandler handler = IOHandler(sandbox);
                if (handler.TryInitializeWithTrackedProcess(fork_event.GetPid()))
                {
                    log_debug("Forced fork event for child PID(%d) and PPID(%d) with path: %{public}s",
                              fork_event.GetChildPid(), fork_event.GetPid(), fork_event.GetExecutablePath());
                    
                    sandbox->GetForceForkedPidMap().emplace(fork_event.GetChildPid(), fork_event.GetPid());
                    handler.HandleEvent(fork_event);
                }
            }
        }
        
        if (handler.TryInitializeWithTrackedProcess(pid))
        {
            size_t msg_length = IOEvent::max_size();
            char msg[msg_length];
            
            omemorystream oms(msg, sizeof(msg));
            oms << event;
            
    #pragma clang diagnostic push
    #pragma clang diagnostic ignored "-Wswitch"
            
            if (!isInterposedEvent)
            {
                switch (event.GetEventType())
                {
                    case ES_EVENT_TYPE_NOTIFY_FORK:
                    {
                        sandbox->SetProcessPidPair(sandbox->GetAllowlistedPidMap(), pid, event.GetParentPid());
                        break;
                    }
                    case ES_EVENT_TYPE_NOTIFY_EXIT:
                        sandbox->RemoveProcessPid(sandbox->GetAllowlistedPidMap(), pid);
                        break;
                }
            }
            
            handler.HandleEvent(event);
            
    #pragma clang diagnostic pop
        }
        else
        {
            // TODO: Delete
            size_t msg_length = IOEvent::max_size();
            char msg[msg_length];
            
            omemorystream oms(msg, sizeof(msg));
            oms << event;
            log_debug("Not tracked: %{public}.*s",(int) event.Size(), msg);
        }
    }
    else
    {
        switch (backing)
        {
            case IOEventBacking::EndpointSecurity: {
                return ProcessCallbackResult::MuteSource;
                break;
            }
            case IOEventBacking::Interposing: {
                assert(false); // Should never happen when interposed events are handled!
                break;
            }
        }
    }
    
    return ProcessCallbackResult::Done;
}

static ProcessCallbackResult process_event(void *handle, const IOEvent event, pid_t host, IOEventBacking backing)
{
    Sandbox* sandbox = (Sandbox *) handle;
#if __APPLE__
    if (sandbox->IsRunningHybrid())
    {
        dispatch_async(sandbox->GetHybridQueue(), ^{
            // TODO: We can't mute processes when merging ES and detours events asynchronously without introducing some async callback
            _process_event(sandbox, event, host, backing);
        });
        
        return ProcessCallbackResult::Done;
    }
    else
#endif
    {
        return _process_event(sandbox, event, host, backing);
    }
}

#endif /* EventProcessor_h */
