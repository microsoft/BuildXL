// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

/**
 * Common arguments for Symbol operations. 
 */
@@public
export interface OperationArguments extends Transformer.RunnerArguments {    
    /** Optional additional dependencies. */
    dependencies?: Transformer.InputArtifact[];
    
    /** Environment variables to forward to SymbolDaemon.exe */
    forwardEnvironmentVars?: string[];
    
    /** Additional environment variables to set before invoking SymbolDaemon.exe */
    additionalEnvironmentVars?: Transformer.EnvironmentVariable[];
    
    /** Directories to untrack. */
    untrackedDirectoryScopes?: Directory[];
    
    /** Process execution timeout, before BuildXL automatically kills it. */
    timeoutInMilliseconds?: number;
    
    /** Process execution warning timeout, before BuildXL issues a warning. */
    warningTimeoutInMilliseconds?: number;

    /** ------------------------------------------------------------------- */
    
    /** Number of retries to connect to a running SymbolDaemon process. */
    maxConnectRetries?: number;
    
    /** Delay between retries to connect to a running SymbolDaemon process. */
    connectRetryDelayMillis?: number;
    
    /** Request name. */
    name?: string;
    
    /** Symbol service config file. */
    symbolServiceConfigFile?: File;
    
    /** Where to save console output. */
    consoleOutput?: Path;
}

/**
 * VSO Symbol settings.
 */
@@public
export interface SymbolRequestSettings {
    /** Service URL. */
    service?: string;

    /** Request name. */
    name?: string;
    
    /** Maximum number of uploads to issue to symbol service in parallel. */
    maxParallelUploads?: number;
    
    /** Retention period in days for uploaded symbols. */
    retentionDays?: number;
    
    /** Enable symbol telemetry. */
    enableTelemetry?: boolean;
}

/**
 * SymbolDaemon settings
 */
@@public
export interface DaemonSettings {
    /** Maximum number of clients SymbolDaemon should process concurrently. */
    maxConcurrentClients?: number;
    
    /** Whether SymbolDaemon should send ETW events about the progress of symbol
     *  operations for the purpose of integration with CloudBuild. 
     */
    enableCloudBuildIntegration?: boolean;
    
    /** Verbose logging. */
    verbose?: boolean;
}

/**
 * Arguments for starting the SymbolDaemon service.
 */
@@public
export interface ServiceStartArguments extends DaemonSettings, OperationArguments {}

/**
 * Arguments for the 'symbold create' operation.
 */
@@public
export interface SymbolCreateArguments extends SymbolRequestSettings, DaemonSettings, OperationArguments {}

/** 
 * Common properties for all 'result' types.
 */
@@public
export interface Result {
    /** All required outputs (so that other pips can take those as dependencies). */
    outputs: DerivedFile[];
}

/**
 * Information about started symbold service (returned as part of ServiceStartResult)
 */
@@public
export interface ServiceStartResult extends Result {
    symbolDaemonId: Transformer.ServiceId;
    ipcMoniker: IpcMoniker;
}

/**
 * Result for the 'symbold create' operation.
 */
@@public
export interface SymbolCreateResult extends Result {
    /** Info about the started service */
    serviceStartInfo: ServiceStartResult;
}

/**
 * Operations provided by a runner.
 */
@@public
export interface SymbolRunner {
    /** Invokes 'symbolc create'. */
    createSymbol: (args: SymbolCreateArguments) => SymbolCreateResult;

    /** 
     * Adds files to symbol. 
     * Preferred method is to use addArtifactsToSymbol.
     */
    addFilesToSymbol: (createResult: SymbolCreateResult, args: OperationArguments, fileInfos: File[]) => Result;
   
    // ------------------------------- for testing only ------------------------------------
    
    /** Attempts to start a SymbolDaemon which doesn't connect to a symbol service (useful for testing). */
    startDaemonNoSymbol: (args: ServiceStartArguments) => ServiceStartResult;
    
    /** Pings the daemon process (connects to it and waits for a response before exiting). */
    pingDaemon: (serviceInfo: ServiceStartResult, args: OperationArguments) => Result;
    
    /** Reads the content of a file */
    testReadFile: (serviceInfo: ServiceStartResult, file: File, args: OperationArguments) => Result;
}

/**
 * SymbolDaemon tool definition template. 
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
