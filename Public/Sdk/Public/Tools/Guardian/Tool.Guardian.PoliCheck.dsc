// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

const guardianPoliCheckConfigFile = createConfigurationFile(poliCheckConfiguration(), p`${guardianConfigFileDirectory.path}/poliCheckConfiguration.gdnconfig`);

export function addPoliCheckCalls(rootDirectory : Directory, guardianToolRoot : StaticDirectory, packageDirectory : StaticDirectory, guardianDrop : Directory, files : File[]) : Transformer.ExecuteResult {
    const poliCheckWorkDir = Context.getNewOutputDirectory("policheck");

    const environmentVariables : Transformer.EnvironmentVariable[] = [
        {name: "PoliCheck.TargetDirectory", value: rootDirectory}
    ];

    const sealRoot = Transformer.sealSourceDirectory(d`${rootDirectory.path}`, Transformer.SealSourceDirectoryOption.allDirectories);
    
    const dependencies = [sealRoot];

    return createGuardianCall(
        guardianToolRoot,
        packageDirectory,
        guardianDrop,
        dependencies,
        `policheck`,
        poliCheckWorkDir,
        a`policheck.sarif`,
        [guardianPoliCheckConfigFile],
        environmentVariables,
        /*retryExitCodes*/undefined,
        /*processRetries*/undefined,
        /*pathDirectories*/undefined,
        /*additionalOutputs*/undefined,
        /*untrackedPaths*/[d`${Context.getMount("ProgramData").path}/Microsoft/Crypto`],
        /*untrackedScopes*/undefined,
        /*allowUndeclaredSourceReads*/false,
        ["COMPUTERNAME"]);
}

function poliCheckConfiguration() : Object {
    return {
        "fileVersion": "1.1.2",
        "tools": [
            {
                "fileVersion": "1.1.2",
                "tool": {
                    "name": "policheck",
                    "version": "7.0.720"
                },
                "arguments": {
                    "Target": "$(PoliCheck.TargetDirectory)",
                    "TermTables": 9,
                    "ExcludeGlobalTermTable": false,
                    "CommentScanning": 0,
                    "SubfolderScanning": 1,
                    "HistoryManagement": 0
                },
                "outputExtension": "xml",
                "successfulExitCodes": [
                    0
                ],
                "errorExitCodes": {
                    "1": "Canceled operation error.",
                    "2": "Invalid arguments error.",
                    "3": "Application error.",
                    "4": "Policheck service error.",
                    "5": "KeyVault operation error.",
                    "6": "OnePolicheck error.",
                    "7": "Infrastructure error.",
                    "8": "Unhandled error.",
                    "9": "Policheck data error.",
                    "255": "The tool crashed. Please contact the tool owner for support."
                },
                "outputPaths": [ ]
            }
        ]
    };
}