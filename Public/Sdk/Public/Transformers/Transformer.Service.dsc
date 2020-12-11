// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Transformer {

    /** Schedules a new service pip. */
    @@public
    export function createService(args: CreateServiceArguments): CreateServiceResult {
        return <CreateServiceResult>_PreludeAmbientHack_Transformer.createService(args);
    }

    /** 
     * Returns a new moniker for any IPC, as provided by BuildXL.Ipc.dll.
     * 
     * A moniker must be provided to every 'ipcSend' call because a moniker is used to identify
     * the communication channel for the inter-process communication.
     */
    @@public
    export function getNewIpcMoniker(): IpcMoniker {
        return _PreludeAmbientHack_Transformer.getNewIpcMoniker();
    }
    
    /**
     * Returns the moniker identifying the BuildXL IPC server.
     */
    @@public
    export function getIpcServerMoniker(): IpcMoniker {
        return _PreludeAmbientHack_Transformer.getDominoIpcServerMoniker();
    }

    /**
     * Returns the moniker identifying the BuildXL IPC server.
     */
    @@public
    @@obsolete("Please start using getIpcServerMoniker")
    export function getDominoIpcServerMoniker(): IpcMoniker {
        return _PreludeAmbientHack_Transformer.getDominoIpcServerMoniker();
    }

    @@public
    export interface CreateServiceArguments extends ExecuteArgumentsCommon {
        /** A command for BuildXL to execute at the end of /phase:Execute
          * to gracefully shut down this service. */
        serviceShutdownCmd?: ExecuteArguments | IpcSendArguments;

        /** A command for BuildXL to schedule after all client pips of this service pip. */
        serviceFinalizationCmds?: (ExecuteArguments | IpcSendArguments)[];

        /** Tag to associate other pips in a build with this service.
          * If a tag is specified, the service will be registered with ServicePipTracker. */
        serviceTrackableTag? : string;

        /** Print-friendly name for the trackable tag. 
          * Must be specified if serviceTrackableTag is specified. */
        serviceTrackableTagDisplayName? : string;
    }

    @@public
    export interface CreateServiceResult extends ExecuteResult {
        /** Unique service pip identifier assigned by BuildXL at creation time.  */
        serviceId: ServiceId;
    }

    @@public
    export interface ServiceId {}
}
