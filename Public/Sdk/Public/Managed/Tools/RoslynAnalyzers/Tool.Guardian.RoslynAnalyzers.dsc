// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Guardian from "Sdk.Guardian";

const guardianRoslynAnalyzersConfigFile = Guardian.createConfigurationFile(roslynAnalyzersConfiguration(), p`${Guardian.guardianConfigFileDirectory.path}/roslynAnalyzersConfiguration.gdnconfig`);

/**
 * Create RoslynAnalyzers Guardian calls with copylogs=true
 * With RoslynAnalyzers enabled, roslynanalyzers run with Csc.exe and produce outputfiles
 * Guardian use RoslynAnalyzers to copy these outputfiles, then do analyze and break operations
 */
@@public
export function createRoslynCalls(logRootDirectory: Directory, files: File[], uniquePath: RelativePath) {
    if (!Environment.hasVariable("TOOLPATH_GUARDIAN")) {
        Contract.fail("Guardian drop root must be provided with the 'TOOLPATH_GUARDIAN' environment variable.");
    }

    const guardianDrop : Directory = Environment.getDirectoryValue("TOOLPATH_GUARDIAN");
    const guardianDirectory : Directory = d`${guardianDrop.path}/gdn`;
    const guardianTool : StaticDirectory = Transformer.sealPartialDirectory(guardianDirectory, globR(guardianDirectory, "*"));

    // Package directory must be partially sealed first
    const packageDirectory : StaticDirectory = Transformer.sealPartialDirectory(
        d`${guardianDrop}/packages`,
        globR(d`${guardianDrop}/packages`, "*").filter(file => file.extension !== a`rex`), // This avoid sealing the .lite.rex files for CredScan to avoid double writes
        [Guardian.guardianTag],
        "Seal Guardian package directory"
    );

    addRoslynAnalyzersCalls(guardianTool, packageDirectory, guardianDrop, files, logRootDirectory, uniquePath);
}

function addRoslynAnalyzersCalls(guardianToolRoot : StaticDirectory, packageDirectory : StaticDirectory, guardianDrop : Directory, files : File[], logRootDirectory: Directory, uniquePath: RelativePath) : Transformer.ExecuteResult { 
    const roslynAnalyzersWorkDir =  Context.getNewOutputDirectory("roslynAnalyzers");
    const environmentVariables : Transformer.EnvironmentVariable[] = [
        {name: "RoslynAnalyzers.LogRootDirectory", value: logRootDirectory}
    ];

    return Guardian.createGuardianCall(
        guardianToolRoot,
        packageDirectory,
        guardianDrop,
        files,
        `roslynanalyzer`,
        roslynAnalyzersWorkDir,
        r`${uniquePath}/roslynanalyzer.sarif`,
        [guardianRoslynAnalyzersConfigFile],
        environmentVariables,
        /*retryExitCodes*/undefined,
        /*processRetries*/undefined,
        /*pathDirectories*/undefined,
        /*additionalOutputs*/undefined,
        /*untrackedPaths*/[d`${guardianToolRoot.path}/.gdn`],
        /*untrackedScopes*/undefined,
        /*allowUndeclaredSourceReads*/false,
        /*passThroughEnvironmentVariables*/undefined);
}

function roslynAnalyzersConfiguration() : Object {
    return {
        "fileVersion": "1.14.0",
        "tools": [
            {
                "fileVersion": "1.14.0",
                "tool": {
                    "name": "roslynanalyzers",
                    "version": "latest"
                },
                "arguments": {
                    "CopyLogsOnly": true,
                    "CodeAnalysisAssemblyVersion": "3.5.0",
                    "CSharpCodeStyleAnalyzersRootDirectory": "",
                    "ForceSuccess": false,
                    "TreatWarningsAsErrors": false,
                    "LogRootDirectory": "$(RoslynAnalyzers.LogRootDirectory)"
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