// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Drop from "Sdk.Drop"; 
import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import {range, cmdExe} from "DistributedIntegrationTests.Utils";

const runner = Drop.runner;

const dropNameEnvVarName = "TF_ROLLING_DROPNAME";

const buildNumber = Environment.hasVariable(dropNameEnvVarName)
    ? Environment.getStringValue(dropNameEnvVarName)
    : undefined;

@@public
export const DropOpaqueDirTest = main(buildNumber ? `buildxl.rolling.dropUploadTest/${buildNumber}` : undefined);

// The test produces an opaque directory, creates a drop, and places the content of the directory inside the drop.
function main(dropName : string) {
    if (dropName === undefined) {
        Debug.writeLine(`*** Skipping DropOpaqueDirTest *** (because env var '${dropNameEnvVarName}' is not defined)`);
    }
    else {
        const batchSize = 10;
        const dropConfig = getConfig(dropName, batchSize);

        const dropCreateResult = Drop.runner.createDrop(dropConfig);

        // create an opaque directory and put some files inside
        const outDir = Context.getNewOutputDirectory("dropOpaqueDirTest");
        const opaqueDir = d`${outDir}/opaqueDirToUpload`;
        const executeResult = Transformer.execute({
            tool: cmdExe,
            arguments: [
                Cmd.argument("/d"),
                Cmd.argument("/c"),
                Cmd.argument(Artifact.input(f`./produceOutput.cmd`)),
                Cmd.argument(Artifact.output(opaqueDir)),
                Cmd.argument(Artifact.output(p`${outDir}/output1`)),
                Cmd.argument(Artifact.output(p`${outDir}/output2`))
            ],
            outputs: [p`${opaqueDir}/1.txt` /* Refer explicitly output in the opaque directory. */],
            workingDirectory: d`.`
        });

        const producedOpaqueDir = executeResult.getOutputDirectory(opaqueDir);
        const directoryToDrop = d`./DirectoryToDrop`;
        const sealedDirectoryToDrop = Transformer.sealDirectory(directoryToDrop, glob(directoryToDrop, "*"));
        const dropOperationArgs = <Drop.DropOperationArguments>
            {
                name : dropConfig.name,
                maxConnectRetries: dropConfig.maxConnectRetries,
                connectRetryDelayMillis: dropConfig.connectRetryDelayMillis
            };

        Drop.runner.addDirectoriesToDrop(
            dropCreateResult,
            dropOperationArgs,
            [ 
                <Drop.DirectoryInfo>
                {
                    dropPath : r`${producedOpaqueDir.name}`,
                    directory: producedOpaqueDir
                },
                <Drop.DirectoryInfo>
                {
                    dropPath : r`DirectoryToDrop`,
                    directory: sealedDirectoryToDrop
                },
                <Drop.DirectoryInfo>
                {
                    dropPath : r`DirectoryToDrop`,
                    directory: sealedDirectoryToDrop
                } 
            ]
        );

        const anotherDropLocation = r`AnotherDropLocation`;
        const outputFilesToDrop = executeResult.getOutputFiles().map(f => Drop.runner.fileInfoToDropFileInfo(<Drop.FileInfo>{ dropPath: r`${anotherDropLocation}/${f.name}`, file: f }));

        Drop.runner.addArtifactsToDrop(
            dropCreateResult,
            dropOperationArgs,
            [
                ...outputFilesToDrop,
                Drop.runner.directoryInfoToDropDirectoryInfo(
                    <Drop.DirectoryInfo>
                    {
                        dropPath : r`${anotherDropLocation}/${producedOpaqueDir.name}`,
                        directory: producedOpaqueDir,
                    }),
                Drop.runner.directoryInfoToDropDirectoryInfo(
                    <Drop.DirectoryInfo>
                    {
                        dropPath : r`${anotherDropLocation}/DirectoryToDrop`,
                        directory: sealedDirectoryToDrop,
                    }),
                Drop.runner.directoryInfoToDropDirectoryInfo(
                    <Drop.DirectoryInfo>
                    {
                        dropPath : r`${anotherDropLocation}/DirectoryToDrop`,
                        directory: sealedDirectoryToDrop,
                    }),
                ...outputFilesToDrop,
            ]
        );
    }
}

function getConfig(dropName : string, batchSize : number) : Drop.DropCreateArguments {
    return <Drop.DropCreateArguments>{
        // DropSettings
        /** Service URL. */
        service: "https://mseng.artifacts.visualstudio.com/DefaultCollection",
        
        /** Size of batches in which to send 'associate' requests to drop service. */
        batchSize: batchSize,
        
        /** Maximum time in milliseconds before triggering a batch 'associate' request. */
        nagleTimeMillis: 2000,
        
        /** Retention period in days for uploaded drops. */
        retentionDays: 1,

        // DaemonSettings
        /** Maximum number of clients DropDaemon should process concurrently. */
        maxConcurrentClients: (batchSize > 1 ? batchSize : 2),
        
        /** Whether DropDaemon should send ETW events about the progress of drop operations for the purpose of integration with CloudBuild. */
        enableCloudBuildIntegration: true,
        
        /** Verbose logging. */
        verbose: true,

        // DropOperationArguments extends CommonArguments
        /** Number of retries to connect to a running DropDaemon process. */
        maxConnectRetries: 20,
        
        /** Delay between retries to connect to a running DropDaemon process. */
        connectRetryDelayMillis: 3000,
        
        /** Request name. */
        name: dropName
    };
}