// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

const guardianFlawFinderConfigFile = createConfigurationFile(flawFinderConfiguration(), p`${guardianConfigFileDirectory.path}/flawFinderConfiguration.gdnconfig`);

export function addFlawFinderCalls(rootDirectory : Directory, guardianToolRoot : StaticDirectory, packageDirectory : StaticDirectory, guardianDrop : Directory, files : File[]) : Transformer.ExecuteResult {  
    const flawFinderWorkDir =  Context.getNewOutputDirectory("flawfinder");

    // Seal the root directory as flawfinder will try to access the whole directory
    const sealRoot = Transformer.sealSourceDirectory(d`${rootDirectory.path}`, Transformer.SealSourceDirectoryOption.allDirectories);

    // Use rootDirectory as the default directory
    const environmentVariables : Transformer.EnvironmentVariable[] = [
        {name: "DefaultFlawfinderTargets", value: rootDirectory}
    ];

    return createGuardianCall(
        guardianToolRoot,
        packageDirectory,
        guardianDrop,
        [sealRoot],
        `flawfinder`,
        flawFinderWorkDir,
        a`flawfinder.sarif`,
        [guardianFlawFinderConfigFile],
        environmentVariables,
        /*retryExitCodes*/undefined,
        /*processRetries*/undefined,
        /*pathDirectories*/undefined,
        /*additionalOutputs*/undefined,
        /*untrackedPaths*/undefined,
        /*untrackedScopes*/undefined,
        /*allowUndeclaredSourceReads*/false,
        /*passThroughEnvironmentVariables*/undefined);
}

function flawFinderConfiguration() : Object {
    return {
        "fileVersion": "1.1.0",
        "tools": [
            {
                "fileVersion": "1.1.0",
                "tool": {
                    "name": "flawfinder",
                    "version": "2.0.15.1"
                },
                "arguments": {
                    "Target": "d|$(DefaultFlawfinderTargets);-:d|**\\.gdn;-:d|**\\out;",
                    "MinLevel": 1,
                    "CSV": false
                },
                "outputExtension": "html",
                "successfulExitCodes": [
                    0,
                    1
                ],
                "outputPaths": [ ]
            }
        ]
    };
}