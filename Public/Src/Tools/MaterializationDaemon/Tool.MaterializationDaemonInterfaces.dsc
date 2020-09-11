// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

/**
 * Common args for service pips.
 */
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

/**
 * Definition of an external manifest parser tool.
 * 
 * The daemon will deligate parsing of manifest files to this tool.
 * For each file, it will launch the tool (start a new process) and will read the response from tool's StdOut.
 * The tool is expected to exit with '0' exit code; any other exit code is interpreted as a tool failure.
 * 
 * The order of arguments:
 * <exePath> /i:<manifest file path> <additionalCommandLineArguments>
 */
@@public
export interface ManifestParserTool {
    /** Executable path. */
    exePath: string;

    /** Optional command line arguments  */
    additionalCommandLineArguments?: string;
}

@@public
export interface ServiceStartArguments extends ConnectionArguments {
    /** Maximum number of files to materialize concurrently */
    maxDegreeOfParallelism? : number;

    /** Environment variables to forward to the daemon */
    forwardEnvironmentVars?: string[];

    /** An external parser. */
    manifestParser?: ManifestParserTool;
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
 * A directory with manifest files.
 */
@@public
export interface ManifestFileDirectory {
    /** Input directory. */
    directory: SharedOpaqueDirectory;

    /** Optional regex that filters in manifest files inside of a provided directory. 
     *  If no filter is specified, the daemon assumes that the directory is already filtered.
    */
    contentFilter?: string;
}

/**
 * Operations provided by a runner.
 */
@@public
export interface MaterializationRunner {
    startDaemon: (args: ServiceStartArguments) => ServiceStartResult;

    loadManifestsAndMaterializeFiles: (startResult: ServiceStartResult, args : ConnectionArguments, directories: ManifestFileDirectory[]) => Result; 
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
