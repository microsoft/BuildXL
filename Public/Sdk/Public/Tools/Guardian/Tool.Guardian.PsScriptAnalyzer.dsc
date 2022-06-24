// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

const guardianPsscriptAnalyzerConfigFile = createConfigurationFile(psscriptAnalyzerConfiguration(), p`${guardianConfigFileDirectory.path}/psscriptAnalyzerConfiguration.gdnconfig`);

export function addPsscriptAnalyzerCalls(rootDirectory : Directory, guardianToolRoot : StaticDirectory, packageDirectory : StaticDirectory, guardianDrop : Directory, files : File[]) : Transformer.ExecuteResult {  
    const psscriptanalyzerWorkDir =  Context.getNewOutputDirectory("psscriptanalyzer");

    // Seal the root directory as psscriptanalyzer will try to access the whole directory
    const sealRoot = Transformer.sealSourceDirectory(rootDirectory, Transformer.SealSourceDirectoryOption.allDirectories);
    const sealEnlistmentDataDir = Transformer.sealSourceDirectory(d`${Environment.getPathValue("BUILDXL_ENLISTMENT_DATA_DIR")}`, Transformer.SealSourceDirectoryOption.allDirectories);
    const dependencies = [sealRoot, sealEnlistmentDataDir, ...globR(d`${Context.getMount("ProgramFiles").path}/WindowsPowerShell/Modules`)];

    // Use rootDirectory as the default directory
    const environmentVariables : Transformer.EnvironmentVariable[] = [
        {name: "PSScriptAnalyzer.DefaultDirectory", value: rootDirectory}
    ];

    return createGuardianCall(
        guardianToolRoot,
        packageDirectory,
        guardianDrop,
        dependencies,
        `psscriptanalyzer`,
        psscriptanalyzerWorkDir,
        r`psscriptanalyzer.sarif`,
        [guardianPsscriptAnalyzerConfigFile],
        environmentVariables,
        /*retryExitCodes*/undefined,
        /*processRetries*/undefined,
        /*pathDirectories*/[d`${Context.getMount("Windows").path}/System32/WindowsPowerShell/v1.0`],
        /*additionalOutputs*/undefined,
        /*untrackedPaths*/undefined,
        /*untrackedScopes*/undefined,
        /*allowUndeclaredSourceReads*/false,
        /*passThroughEnvironmentVariables*/undefined);
}

function psscriptAnalyzerConfiguration() : Object {
    return {
        "fileVersion": "1.0",
        "tools": [
            {
                "fileVersion": "1.0",
                "tool": {
                    "name": "psscriptanalyzer",
                    "version": "1.19.1.9"
                },
                "arguments": {
                    "Path": "$(PSScriptAnalyzer.DefaultDirectory)",
                    "Recurse": true,
                    "Settings": "$(SDLRequiredConfigurationFile)",
                    "IgnorePattern": ".gdn"
                },
                "outputExtension": "sarif",
                "successfulExitCodes": [
                    0
                ],
                "outputPaths": [ ]
            }
        ]
    };
}