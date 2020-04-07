// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef EventProcessor_h
#define EventProcessor_h

#include "IOEvent.hpp"
#include "IOHandler.hpp"
#include "Sandbox.hpp"

extern Sandbox *sandbox;

static void process_event(es_client_t *client, const IOEvent &event, pid_t host, IOEventBacking backing)
{
    pid_t pid = event.GetPid();
    if (pid == host)
    {
        return;
    }
    
    bool ppid_found = sandbox->GetPidMap().find(event.GetParentPid()) != sandbox->GetPidMap().end();
    auto original_ppid_found = sandbox->GetPidMap().find(event.GetOriginalParentPid()) != sandbox->GetPidMap().end();
    
    if (ppid_found || original_ppid_found)
    {
        IOHandler handler = IOHandler(sandbox);
        if (handler.TryInitializeWithTrackedProcess(pid))
        {
    #pragma clang diagnostic push
    #pragma clang diagnostic ignored "-Wswitch"
            switch (event.GetEventType())
            {
                case ES_EVENT_TYPE_NOTIFY_FORK:
                    sandbox->GetPidMap().emplace(event.GetPid(), true);
                    break;

                case ES_EVENT_TYPE_NOTIFY_EXIT:
                    sandbox->RemoveWhitelistedPid(pid);
                    break;
            }
            handler.HandleEvent(event);
    #pragma clang diagnostic pop
        }
    }
    else
    {
        switch (backing)
        {
            case IOEventBacking::EndpointSecurity: {
                es_mute_process(client, event.GetProcessAuditToken());
                break;
            }
            case IOEventBacking::Interposing: {
                assert(false); // Should never happen when interposed events are handled!
                break;
            }
        }
    }
}

#endif /* EventProcessor_h */
