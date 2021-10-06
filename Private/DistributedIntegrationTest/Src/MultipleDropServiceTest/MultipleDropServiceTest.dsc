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
export const DropOpaqueDirTest = main(buildNumber ? `buildxl.rolling.multipleDropServiceTest/${buildNumber}` : undefined);

// The test produces an opaque directory, creates a drop, and places the content of the directory inside the drop.
function main(dropName : string) {
    if (dropName === undefined) {
        Debug.writeLine(`*** Skipping MultipleDropServiceTest *** (because env var '${dropNameEnvVarName}' is not defined)`);
    }
    else {
        // Create service
        const serviceConfig = getServiceStartArgs();
        const serviceStartResult = Drop.runner.startService(serviceConfig);

        // Create first drop
        const firstDropConfig = getConfig(dropName);
        const firstDropCreateResult = Drop.runner.createDropUnderService(serviceStartResult, firstDropConfig);
        
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
        const sealedDirectoryToDrop = Transformer.sealDirectory({
            root: directoryToDrop, 
            files: glob(directoryToDrop, "*")
        });
        
        const firstDropOperationArgs = <Drop.DropOperationArguments>
            {
                name : firstDropConfig.name,
                maxConnectRetries: firstDropConfig.maxConnectRetries,
                connectRetryDelayMillis: firstDropConfig.connectRetryDelayMillis
            };

        const firstDropResult1 = Drop.runner.addDirectoriesToDrop(
            firstDropCreateResult,
            firstDropOperationArgs,
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

        // Add new files using addFiles
        const firstDropResult2 = Drop.runner.addFilesToDrop(firstDropCreateResult, firstDropOperationArgs, outputFilesToDrop);

        // Finalize the first drop explicitly
        Drop.runner.finalizeDrop(
            firstDropCreateResult, 
            firstDropOperationArgs, 
            [firstDropResult1, firstDropResult2]);

        // Create second drop
        const secondDropConfig = getConfig(dropName + "_2");
        const secondDropCreateResult = Drop.runner.createDropUnderService(serviceStartResult, secondDropConfig);
        const secondDropOperationArgs = <Drop.DropOperationArguments>
            {
                name : secondDropConfig.name,
                maxConnectRetries: secondDropConfig.maxConnectRetries,
                connectRetryDelayMillis: secondDropConfig.connectRetryDelayMillis
            };

        // Drop files to second drop
        Drop.runner.addArtifactsToDrop(
            secondDropCreateResult,
            secondDropOperationArgs,
            [
                // Test duplicate file adding
                ...outputFilesToDrop,
                ...outputFilesToDrop,
            ]
        );
        
        // Do not finalize the second drop explicitly, the service will do it automatically on shutdown
    }
}

function getServiceStartArgs() : Drop.ServiceStartArguments {
    return <Drop.ServiceStartArguments>{       
        /** Whether DropDaemon should send ETW events about the progress of drop operations for the purpose of integration with CloudBuild. */
        enableCloudBuildIntegration: true,
        
        /** Verbose logging. */
        verbose: true,
    };
}

function getConfig(dropName : string) : Drop.DropCreateArguments {
    return <Drop.DropCreateArguments>{
        // DropSettings
        /** Request name. */
        name: dropName,

        /** Service URL. */
        service: "https://mseng.artifacts.visualstudio.com/DefaultCollection",
        
        /** Maximum time in milliseconds before triggering a batch 'associate' request. */
        nagleTimeMillis: 2000,
        
        /** Retention period in days for uploaded drops. */
        retentionDays: 1,
    };
}