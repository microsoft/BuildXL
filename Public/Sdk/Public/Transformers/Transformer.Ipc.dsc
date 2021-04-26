// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Transformer {
    /** Schedules a new Ipc pip. */
    @@public
    export function ipcSend(args: IpcSendArguments): IpcSendResult {
        return <IpcSendResult>_PreludeAmbientHack_Transformer.ipcSend(args);
    }

    /**
     * Arguments for the 'Transformer.ipcSend' function.
     */
    @@public
    export interface IpcSendArguments {
        /** Opaque identifier for the IPC channel. */
        moniker: IpcMoniker;

        /** Command-line arguments to be rendered as a string and used as the IPC message body. */
        messageBody: Argument[];

        /** Service pip that this IPC call will be directed at. */
        targetService: ServiceId;

        /** Output file to write the result to.  If not specified, a default output file name is used.*/
        outputFile?: Path;

        /** Additional input dependencies. */
        fileDependencies?: InputArtifact[];

        /** Maximum number of retries to establish a connection. */        
        maxConnectRetries?: number;

        /** Delay in milliseconds between two consecutive retries to establish a connection. */
        connectRetryDelayMillis?: number;

        /** 
         * Artifact not to materialize eagerly.  
         * 
         * IPC pips may want to use this option when they will explicitly request artifact materialization
         * from BuildXL (via a BuildXL service identified by the Transformer.getIpcServerMoniker()
         * moniker) just before the artifacts are needed. This makes sense for pips that expect that often
         * times they will not have to access the actual files on disk.
         * 
         * Another example where it might be needed is when an ipc pip depends on a directory but actually
         * needs only a subset of files from that directory. By marking this directory for lazy materialization,
         * an ipc pip can only bring the required files from cache.
         */
        lazilyMaterializedDependencies?: Input[];

        /**
         * Whether this pip must execute on the orchestrator node in a distributed build.  Defaults to false.
         */
        mustRunOnOrchestrator?: boolean;

        @@obsolete("Please use mustRunOnOrchestrator")
        mustRunOnMaster?: boolean;

        /** Arbitrary tags */
        tags?: string[];
    }

    /**
     * Result of the 'ipcSend' function.
     */
    @@public
    export interface IpcSendResult {
        outputFile: DerivedFile;
    }

    /**
     * Opaque type representing a moniker used for inter-process communication (IPC).
     * 
     * A value of this type should not be created directly; instead, always use Transformer.getNewIpcMoniker().
     */
    @@public
    export interface IpcMoniker {
        __ipcMonikerBrand: any;
    }

}
