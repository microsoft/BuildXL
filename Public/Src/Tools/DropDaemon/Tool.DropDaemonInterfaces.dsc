// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

/**
 * Common arguments for all DropDaemon operations
 */
@@public
export interface CommonArguments extends Transformer.RunnerArguments {
    /** Optional additional dependencies. */
    dependencies?: Transformer.InputArtifact[];
    
    /** Environment variables to forward to DropDaemon.exe */
    forwardEnvironmentVars?: string[];
    
    /** Additional environment variables to set before invoking DropDaemon.exe */
    additionalEnvironmentVars?: Transformer.EnvironmentVariable[];
    
    /** Directories to untrack. */
    untrackedDirectoryScopes?: Directory[];
    
    /** Process execution timeout, before BuildXL automatically kills it. */
    timeoutInMilliseconds?: number;
    
    /** Process execution warning timeout, before BuildXL issues a warning. */
    warningTimeoutInMilliseconds?: number;
}

/**
 * VSO Drop settings.
 */
@@public
export interface DropSettings {
    /** Service URL. */
    service?: string;
    
    /** Size of batches in which to send 'associate' requests to drop service. */
    batchSize?: number;
    
    /** Maximum time in milliseconds before triggering a batch 'associate' request. */
    nagleTimeMillis?: number;
    
    /** Maximum number of uploads to issue to drop service in parallel. */
    maxParallelUploads?: number;
    
    /** Retention period in days for uploaded drops. */
    retentionDays?: number;
    
    /** Enable drop telemetry. */
    enableTelemetry?: boolean;
}

/**
 * DropDaemon settings
 */
@@public
export interface DaemonSettings {
    /** Maximum number of clients DropDaemon should process concurrently. */
    maxConcurrentClients?: number;
    
    /** Whether DropDaemon should send ETW events about the progress of drop
     *  operations for the purpose of integration with CloudBuild. */
    enableCloudBuildIntegration?: boolean;
    
    /** Verbose logging. */
    verbose?: boolean;
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
 * Arguments for starting the DropDaemon service.
 */
@@public
export interface ServiceStartArguments extends DaemonSettings, CommonArguments {}

/**
 * Information about started dropd service (returned as part of ServiceStartResult)
 */
@@public
export interface ServiceStartResult extends Result {
    dropDaemonId: Transformer.ServiceId;
    ipcMoniker: IpcMoniker;
}

/**
 * Common arguments for Drop operations. 
 */
@@public
export interface DropOperationArguments extends CommonArguments {
    /** Number of retries to connect to a running DropDaemon process. */
    maxConnectRetries?: number;
    
    /** Delay between retries to connect to a running DropDaemon process. */
    connectRetryDelayMillis?: number;
    
    /** Request name. */
    name?: string;
    
    /** Drop service config file. */
    dropServiceConfigFile?: File;
    
    /** Where to save console output. */
    consoleOutput?: Path;
}

/**
 * Arguments for the 'dropd create' operation.
 */
@@public
export interface DropCreateArguments extends DropSettings, DaemonSettings, DropOperationArguments {}

/**
 * Result for the 'dropd create' operation.
 */
@@public
export interface DropCreateResult extends Result {
    /** Info about the started service */
    serviceStartInfo: ServiceStartResult;
}

/**
 * Arguments for the 'dropd addartifacts' operation.
 */

interface DropArtifactInfoBase {
    /** Relative path in drop. */
    dropPath: RelativePath;
}

/**
 * Arguments for the 'dropd addartifacts' operation for dropping file.
 */
@@public
export interface DropFileInfo extends DropArtifactInfoBase {
    /** Artifact kind */
    kind: "file";
    
    /** Input file to add to drop. */
    file: File;
}

/**
 * Arguments for the 'dropd addartifacts' operation for dropping directory.
 */
@@public
export interface DropDirectoryInfo extends DropArtifactInfoBase {
    /** Artifact kind */
    kind: "directory";

    /** Input directory to add to drop. */
    directory: StaticDirectory;

    /** regex filter */
    contentFilter?: string;
}

@@public
export type DropArtifactInfo = DropFileInfo | DropDirectoryInfo;

//////////// Legacy types, preserved to maintain back compatibility
/**
 * Base interface for drop artifact info.
 */
@@public
export interface ArtifactInfo {

    /** Relative path in drop. */
    dropPath: RelativePath;
}

/**
 * Arguments for the 'dropd addfile' operation.
 */
@@public
export interface FileInfo extends ArtifactInfo {
    
    /** Input file to add to drop. */
    file: File;
}

/**
 * Arguments for the 'dropd adddirectories' operation.
 */
@@public
export interface DirectoryInfo extends ArtifactInfo {

    /** Input directory to add to drop. */
    directory: StaticDirectory;

    /** regex filter */
    contentFilter?: string;
}
//////////// Legacy types, preserved to maintain back compatibility

/**
 * Operations provided by a runner.
 */
@@public
export interface DropRunner {
    /** Invokes 'dropc create'. */
    createDrop: (args: DropCreateArguments) => DropCreateResult;

    /** 
     * Adds files to drop. 
     * Preferred method is to use addArtifactsToDrop.
     */
    addFilesToDrop: (createResult: DropCreateResult, args: DropOperationArguments, fileInfos: FileInfo[]) => Result;
    
    /** 
     * Adds directories to drop. 
     * Preferred method is to use addArtifactsToDrop.
     * If used, directoryContentFilter must specify a .Net-style case-insensitive regex.
     */
    addDirectoriesToDrop: (createResult: DropCreateResult, args: DropOperationArguments, directories: DirectoryInfo[], directoryContentFilter? : string) => Result;

    /** 
     * Adds artifacts to drop.
     * If used, directoryContentFilter must specify a .Net-style case-insensitive regex.
     */
    addArtifactsToDrop: (createResult: DropCreateResult, args: DropOperationArguments, artifacts: DropArtifactInfo[], directoryContentFilter? : string) => Result;

    // ------------------------------- for legacy type conversion --------------------------

    /** Converts file info to drop file info. */
    fileInfoToDropFileInfo: (fileInfo: FileInfo) => DropFileInfo;

    /** Converts directory info to drop directory info. */
    directoryInfoToDropDirectoryInfo: (directoryInfo: DirectoryInfo) => DropDirectoryInfo;

    // ------------------------------- for testing only ------------------------------------
    
    /** Attempts to start a DropDaemon which doesn't connect to a drop service (useful for testing). */
    startDaemonNoDrop: (args: ServiceStartArguments) => ServiceStartResult;
    
    /** Pings the daemon process (connects to it and waits for a response before exiting). */
    pingDaemon: (serviceInfo: ServiceStartResult, args: DropOperationArguments) => Result;
    
    /** Reads the content of a file */
    testReadFile: (serviceInfo: ServiceStartResult, file: File, args: DropOperationArguments) => Result;
}

/**
 * DropDaemon tool definition template. 
 * (added to minimize code duplication between 'Tool.DropDaemonTool.dsc' and 'LiteralFiles/Tool.DropDaemonTool.dsc.literal')
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
