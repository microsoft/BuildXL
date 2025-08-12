// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

/**
 * Common args for service pips.
 */
@@public
export interface ConnectionArguments extends Transformer.RunnerArguments{
    /** Number of retries to connect to a running BlobDaemon process. */
    maxConnectRetries?: number;

    /** Delay between retries to connect to a running BlobDaemon process. */
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
    /** Maximum number of files to upload concurrently */
    maxDegreeOfParallelism? : number;

    /** Environment variables to forward to the daemon */
    forwardEnvironmentVars?: string[];
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
    blobDaemonId: Transformer.ServiceId;
    ipcMoniker: IpcMoniker;
}

@@public
export interface UploadLocationUri {
    /** Upload location kind */
    kind: "uri";

    /** URI of the upload location */
    uri: string;
}

@@public
export interface UploadLocationContainer {
    /** Upload location kind */
    kind: "container";

    /** Name of the storage account */
    accountName: string;

    /** Name of the container */
    containerName: string;

    /** Relative path in storage. */
    uploadPath: RelativePath;
}

@@public
export type UploadLocation = UploadLocationUri | UploadLocationContainer;

interface BlobArtifactInfoBase {
    /** Upload location information */
    uploadLocation: UploadLocation;

    /** Name of an environment variable that contains a secret. BlobDaemon will use this variable
     *  to authenticate with the storage account.
     *  
     *  Note: This env variable must also be forwarded by BuildXL to the daemon, i.e., it must be
     *  included in forwardEnvironmentVars.
     * 
     * Auth variable is declared on each artifact. This is intentional - this way one daemon can
     * upload artifact to different locations that might require different authentication.
     */
    authEnvironmentVariable: string;
}

/**
 * Arguments for the 'uploadArtifacts' operation for uploading a file.
 */
@@public
export interface BlobFileInfo extends BlobArtifactInfoBase {
    /** Artifact kind */
    kind: "file";

    /** Input file to add to upload. */
    file: File;
}

/** Arguments for changing a relative path of a file before uploading it. */
@@public
export interface RelativePathReplacementArguments {

    /** string to search for */
    oldValue: string;

    /** string to replace with */
    newValue: string;
}

/**
 * Arguments for the 'uploadArtifacts' operation for uploading a directory.
 */
@@public
export interface BlobDirectoryInfo extends BlobArtifactInfoBase {
    /** Artifact kind */
    kind: "directory";

    /** Input directory to add to upload. */
    directory: StaticDirectory;

    /** 
     * Optional file path regex pattern that specifies which files from this
     * directory should be processed. 
     * 
     * (The filter is applied to the original file name)
     */
    contentFilter?: string;

    /**
     * Whether to apply content filter to file's relative path instead of the full path.
     * Defaults to 'false'.
     * 
     * Note: relative path does not start with directory separator character, i.e., given 
     * a directory "C:\a\" and a file "C:\a\b.txt", the provided regex will be matched
     * against "b.txt" and not "\b.txt".
     * 
     * If set to true, use \G anchor instead of ^ anchor to match the beginning of a relative path.
     */
    applyContentFilterToRelativePath?: boolean;

    /** 
     * Optional relative path replace arguments.
     * 
     * If specified, the replacement is performed on a relative path of
     * each file that is being uploaded when the daemon calculates
     * the final upload destination path.
     * 
     * For example:
     *                     directory: C:\a\
     *                         files: C:\a\1.txt
     *                                C:\a\b\2.txt
     *                                C:\a\c\3.txt
     *          replacementArguments: "b\" -> "c\"
     *                    uploadPath: "b"
     *         
     * files in storage after upload: b/1.txt    <- "b" is not a part of file's ('C:\a\1.txt') relative path ('1.txt'), 
     *                                              so it's not affected by the replacement
     *                                b/c/2.txt  <- file's relative path ('b\2.txt') was changed
     *                                b/c/3.txt  <- file's relative path ('c\2.txt') did not match the search pattern,
     *                                              so it was not modified
     */
    relativePathReplacementArguments?: RelativePathReplacementArguments;   
}

@@public
export type BlobArtifactInfo = BlobFileInfo | BlobDirectoryInfo;

/**
 * Operations provided by a runner.
 */
@@public
export interface BlobRunner {
    /** Start the daemon process. */
    startDaemon: (args: ServiceStartArguments) => ServiceStartResult;

    /** Upload specified directory artifacts. */
    uploadArtifacts: (startResult: ServiceStartResult, args: ConnectionArguments, artifacts: BlobArtifactInfo[]) => Result;
}

/**
 * Tool definition template. 
 */
@@public
export const toolTemplate = <Transformer.ToolDefinition>{
    exe: undefined,
    dependsOnWindowsDirectories: true,
    dependsOnAppDataDirectory: true,
    prepareTempDirectory: true,
};
