// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

@@public
export interface ConnectionArguments extends Transformer.RunnerArguments{
    /** Number of retries to connect to a running MaterializationDaemon process. */
    maxConnectRetries?: number;

    /** Delay between retries to connect to a running MaterializationDaemon process. */
    connectRetryDelayMillis?: number;

    /** Directories to untrack. */
    untrackedDirectoryScopes?: Directory[];
    
    /** Optional additional dependencies. */
    dependencies?: Transformer.InputArtifact[];

    /** Process execution timeout, before BuildXL automatically kills it. */
    timeoutInMilliseconds?: number;
    
    /** Process execution warning timeout, before BuildXL issues a warning. */
    warningTimeoutInMilliseconds?: number;

     /** Where to save console output. */
     consoleOutput?: Path;
}

@@public
export interface ServiceStartArguments extends ConnectionArguments {
    /** Maximum number of files to materialize concurrently */
    maxDegreeOfParallelism? : number;
}

/** 
 * Common properties for all 'result' types.
 */
@@public
export interface Result {
    /** All required outputs (so that other pips can take those as dependencies). */
    outputs: DerivedFile[];
}

/**
 * Information about started service
 */
@@public
export interface ServiceStartResult extends Result {
    materializationDaemonId: Transformer.ServiceId;
    ipcMoniker: IpcMoniker;
}

/**
 * Operations provided by a runner.
 */
@@public
export interface MaterializationRunner {
    startDaemon: (args: ServiceStartArguments) => ServiceStartResult;

    loadManifestsAndMaterializeFiles: (startResult: ServiceStartResult, args : ConnectionArguments, directories: SharedOpaqueDirectory[]) => Result; 
}

/**
 * Tool definition template. 
 */
@@public
export const toolTemplate = <Transformer.ToolDefinition>{
    exe: undefined,
    untrackedDirectoryScopes: [
        Context.getUserHomeDirectory(),
        d`${Context.getMount("ProgramData").path}`,
    ],
    dependsOnWindowsDirectories: true,
    dependsOnAppDataDirectory: true,
    prepareTempDirectory: true,
};
