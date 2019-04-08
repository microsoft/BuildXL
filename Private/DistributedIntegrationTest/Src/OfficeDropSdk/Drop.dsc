// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

/**
 * This SDK spec is a wrapper around DropRunner.
 * There should be no organizational specifics in the wrapper.
 */

import * as DropRunner from "Sdk.Drop";

/**
 * Data needed for performing drop operations.
 */
@@public
export interface DropData {
    /** Drop service config file. */
    dropServiceConfigFile: File;

    /** Drop id obtained from starting a service. */
    dropId: Transformer.ServiceId;

    /** File produced during drop service creation so that others can take dependency on it. */
    createOutputs: DerivedFile[];
}

/**
 * Starts and creates drop.
 */
@@public
export function startDrop(dropServiceConfigFile: File): DropData {
    // Get finalize-shutdown command.
    const shutdownCmd = DropRunner.getFinalizeDropAndStopDaemonCommand({
        dropServiceConfigFile: dropServiceConfigFile
    });

    // Start synchronous service.
    const startTask = DropRunner.startDropDaemonSync(
        {
            dropServiceConfigFile: dropServiceConfigFile,
        }, 
        shutdownCmd);

    // Create drop (depends on 'startTask' being started)
    const createTask = DropRunner.createDrop({
        dropServiceConfigFile: dropServiceConfigFile,
        dropDaemonId: startTask.dropDaemonId,
        maxTcpConnectRetries: 4, 
        tcpConnectRetryDelayMillis: 3000
    });

    return {
        dropServiceConfigFile: dropServiceConfigFile,
        dropId: startTask.dropDaemonId,
        createOutputs: createTask.outputs
    };
}

/**
 * Drops file.
 */
@@public
export function dropFile(dropData: DropData, dropPath: RelativePath, dropFile: File, tags?: string[]): DerivedFile[] {
    const addFileTask = DropRunner.addFileToDrop({
        tags: tags,
        dropServiceConfigFile: dropData.dropServiceConfigFile,
        dropDaemonId: dropData.dropId,
        dropPath: dropPath,
        file: dropFile, 
        dependencies: dropData.createOutputs
    });

    return addFileTask.outputs;
}

@@public
export function dropFiles(dropData: DropData, files: [RelativePath, File][], tags?: string[]): DerivedFile[] {
    const addFileTask = DropRunner.addFilesToDrop({
        tags: tags,
        dropServiceConfigFile: dropData.dropServiceConfigFile,
        dropDaemonId: dropData.dropId,
        dependencies: dropData.createOutputs,
        dropPath: undefined,
        file: undefined
    }, files);

    return addFileTask.outputs;
}
