// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef EventProcessor_h
#define EventProcessor_h

#include <EndpointSecurity/EndpointSecurity.h>

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
                case ES_EVENT_TYPE_NOTIFY_EXEC:
                    return handler.HandleProcessExec(event);
                    
                case ES_EVENT_TYPE_NOTIFY_FORK: {
                    sandbox->GetPidMap().emplace(event.GetPid(), true);
                    return handler.HandleProcessFork(event);
                }
                                                 
                case ES_EVENT_TYPE_NOTIFY_EXIT: {
                    sandbox->RemoveWhitelistedPid(pid);
                    return handler.HandleProcessExit(event);
                }
                    
                case ES_EVENT_TYPE_NOTIFY_LOOKUP:
                    return handler.HandleLookup(event);
                    
                case ES_EVENT_TYPE_NOTIFY_OPEN:
                    return handler.HandleOpen(event);
                    
                case ES_EVENT_TYPE_NOTIFY_CLOSE:
                    return handler.HandleClose(event);

                case ES_EVENT_TYPE_NOTIFY_CREATE:
                    return handler.HandleCreate(event);
                    
                case ES_EVENT_TYPE_NOTIFY_TRUNCATE:
                case ES_EVENT_TYPE_NOTIFY_SETATTRLIST:
                case ES_EVENT_TYPE_NOTIFY_SETEXTATTR:
                case ES_EVENT_TYPE_NOTIFY_DELETEEXTATTR:
                case ES_EVENT_TYPE_NOTIFY_SETFLAGS:
                case ES_EVENT_TYPE_NOTIFY_SETOWNER:
                case ES_EVENT_TYPE_NOTIFY_SETMODE:
                case ES_EVENT_TYPE_NOTIFY_WRITE:
                case ES_EVENT_TYPE_NOTIFY_UTIMES:
                case ES_EVENT_TYPE_NOTIFY_SETTIME:
                case ES_EVENT_TYPE_NOTIFY_SETACL:
                    return handler.HandleGenericWrite(event);
                    
                case ES_EVENT_TYPE_NOTIFY_READDIR:
                case ES_EVENT_TYPE_NOTIFY_FSGETPATH:
                    return handler.HandleGenericRead(event);
                
                case ES_EVENT_TYPE_NOTIFY_GETATTRLIST:
                case ES_EVENT_TYPE_NOTIFY_GETEXTATTR:
                case ES_EVENT_TYPE_NOTIFY_LISTEXTATTR:
                case ES_EVENT_TYPE_NOTIFY_ACCESS:
                case ES_EVENT_TYPE_NOTIFY_STAT:
                    return handler.HandleGenericProbe(event);
                    
                case ES_EVENT_TYPE_NOTIFY_CLONE:
                    return handler.HandleClone(event);
                    
                case ES_EVENT_TYPE_NOTIFY_EXCHANGEDATA:
                    return handler.HandleExchange(event);

                case ES_EVENT_TYPE_NOTIFY_RENAME:
                    return handler.HandleRename(event);
                    
                case ES_EVENT_TYPE_NOTIFY_READLINK:
                    return handler.HandleReadlink(event);
                    
                case ES_EVENT_TYPE_NOTIFY_LINK:
                    return handler.HandleLink(event);
                        
                case ES_EVENT_TYPE_NOTIFY_UNLINK:
                    return handler.HandleUnlink(event);
            }
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
