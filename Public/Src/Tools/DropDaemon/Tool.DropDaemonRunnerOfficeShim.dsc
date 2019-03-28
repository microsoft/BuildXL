// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

const runner = DropDaemonRunner.cloudBuildRunner;
const connectArgs = <DropOperationArguments>{maxConnectRetries: 20, connectRetryDelayMillis: 3000};

//@@obsolete("No need to call this function at all; just call 'createDrop' instead.")
@@public
export function getFinalizeDropAndStopDaemonCommand(args: any): any {
    return undefined;
}
//@@obsolete("No need to call this function at all; just call 'createDrop' instead.")
@@public
export function startDropDaemonSync(args: any, shutdownCommand: any): OldStartResult {
    return <OldStartResult>{
        dropDaemonId: "dummy",
        // won't be used ever
    };
}
//@@obsolete("Don't call this function directly; choose a runner instance exported by this module ('runner' or 'cloudBuildRunner') and call 'createDrop' on it.")
@@public
export function createDrop(args: OldCreateArgs): OldCreateResult {
    const createResult: DropCreateResult = runner.createDrop(
        connectArgs.override<DropCreateArguments>({dropServiceConfigFile: args.dropServiceConfigFile})
    );
    return <OldCreateResult>{
        outputs: createResult as any,
        // mask DropCreateResult as outputs: DerivedFile[]
    };
}
//@@obsolete("Don't call this function directly; choose a runner instance exported by this module ('runner' or 'cloudBuildRunner') and call 'addFilesToDrop' on it.")
@@public
export function addFileToDrop(args: OldAddFileArgs): OldAddFileResult {
    const fileInfo: [RelativePath, File] = [args.dropPath, args.file];
    return addFilesToDrop(args, [fileInfo]);
}
//@@obsolete("Don't call this function directly; choose a runner instance exported by this module ('runner' or 'cloudBuildRunner') and call 'addFilesToDrop' on it.")
@@public
export function addFilesToDrop(args: OldAddFileArgs, files: [RelativePath, File][]): OldAddFileResult {
    return runner.addFilesToDrop(
        extractDropCreateResultFromOldAddFileArgs(args),
        connectArgs.override<DropOperationArguments>({tags: args.tags}),
        files.map(f => <FileInfo>{file: f[1], dropPath: f[0]})
    );
}

function extractDropCreateResultFromOldAddFileArgs(args: OldAddFileArgs): DropCreateResult {
    // treat args.dependencies, which has declared type DerivedFile[] as DropCreateResult,
    // because in 'createDrop' we masked a DropCreateResult object as DerivedFile[]
    return <DropCreateResult><any>args.dependencies;
}

@@public
export interface OldCreateArgs {
    dropServiceConfigFile: File;
    dropDaemonId: Transformer.ServiceId;
    maxTcpConnectRetries?: number;
    tcpConnectRetryDelayMillis?: number;
}

@@public
export interface OldCreateResult {
    outputs: DerivedFile[];
}

@@public
export interface OldAddFileArgs {
    dropServiceConfigFile: File;
    tags?: string[];
    dropDaemonId: Transformer.ServiceId;
    dropPath: RelativePath;
    file: File;
    dependencies?: Transformer.InputArtifact[];
    maxTcpConnectRetries?: number;
    tcpConnectRetryDelayMillis?: number;
}

@@public
export interface OldAddFileResult {
    outputs: DerivedFile[];
}

@@public
export interface OldStartResult {
    dropDaemonId: Transformer.ServiceId;
}
