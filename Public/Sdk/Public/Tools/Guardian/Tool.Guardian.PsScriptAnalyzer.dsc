// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

const guardianPsscriptAnalyzerConfigFile = createConfigurationFile(psscriptAnalyzerConfiguration(), p`${guardianConfigFileDirectory.path}/psscriptAnalyzerConfiguration.gdnconfig`);

export function addPsscriptAnalyzerCalls(rootDirectory : Directory, guardianToolRoot : StaticDirectory, packageDirectory : StaticDirectory, guardianDrop : Directory, files : File[]) : Transformer.ExecuteResult[] {  
    let results : MutableSet<Transformer.ExecuteResult> = MutableSet.empty<Transformer.ExecuteResult>();

    const directoryAtomsToIgnore = Set.create<PathAtom>(
        // Defaults
        a`.git`,
        a`.cloudbuild`,
        a`.corext`,
        a`out`,
        a`node_modules`,
        a`.gdn`,
        // User specified
        ...addIfLazy(Environment.hasVariable(directoriesNamesToIgnore), () => {
            const directoryList = Environment.getStringValue(directoriesNamesToIgnore).split(",");

            return directoryList.map(dir => Context.getCurrentHost().os === "win" ? a`${dir.toLowerCase()}` : a`${dir}`);
        })
    );
    const directoryPathsToIgnore = Set.create<Directory>(
        d`${Context.getMount("SourceRoot").path}/common/temp` // well known path for rush install (not part of initially checked out sources)
    );

    const powershellDependenciesFiles = globR(d`${Context.getMount("ProgramFiles").path}/WindowsPowerShell/Modules`);

    let directories = globFolders(rootDirectory, "*", /*recursive*/false);

    let directoryIndex = 0;
    let i = 0;
    while (directoryIndex < directories.length) {
        const directoryAtom = Context.getCurrentHost().os === "win" ? a`${directories[directoryIndex].name.toString().toLowerCase()}` : directories[directoryIndex].name;
        if (directoryAtomsToIgnore.contains(directoryAtom) || directoryPathsToIgnore.contains(directories[directoryIndex])) {
            directoryIndex++;
            continue;
        }

        const psscriptanalyzerWorkDir =  Context.getNewOutputDirectory("psscriptanalyzer");
        const sealScanDir = Transformer.sealPartialDirectory(directories[directoryIndex], globR(directories[directoryIndex]));
        const dependencies = [sealScanDir, ...powershellDependenciesFiles];
        // Use current scanned directory as the default directory
        const environmentVariables : Transformer.EnvironmentVariable[] = [
            {name: "PSScriptAnalyzer.DefaultDirectory", value: directories[directoryIndex]}
        ]; 
        const sarifName = `psscriptanalyzer_${i.toString()}.sarif`;

        results.add(createGuardianCall(
            guardianToolRoot,
            packageDirectory,
            guardianDrop,
            dependencies,
            `psscriptanalyzer_${i}`,
            psscriptanalyzerWorkDir,
            r`${sarifName}`,
            [guardianPsscriptAnalyzerConfigFile],
            environmentVariables,
            /*retryExitCodes*/undefined,
            /*processRetries*/undefined,
            /*pathDirectories*/[d`${Context.getMount("Windows").path}/System32/WindowsPowerShell/v1.0`],
            /*additionalOutputs*/undefined,
            /*untrackedPaths*/undefined,
            /*untrackedScopes*/undefined,
            /*allowUndeclaredSourceReads*/false,
            /*passThroughEnvironmentVariables*/undefined
            )
        );
        directoryIndex++;
        i++;
    }

    return results.toArray();        
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
                    "Settings": "$(SDLRequiredConfigurationFile)"
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