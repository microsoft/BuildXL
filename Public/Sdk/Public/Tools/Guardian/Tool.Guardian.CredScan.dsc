// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

const guardianCredScanConfigFile = createConfigurationFile(credScanConfiguration(), p`${guardianConfigFileDirectory.path}/credScanConfiguration.gdnconfig`);

/**
 * Adds a set of batched credscan guardian calls.
 */ 
export function addCredScanCalls(rootDirectory : Directory, guardianToolRoot : StaticDirectory, packageDirectory : StaticDirectory, guardianDrop : Directory, files : File[]) : Transformer.ExecuteResult[] {    
    let results : MutableSet<Transformer.ExecuteResult> = MutableSet.empty<Transformer.ExecuteResult>();
    let directories = globFolders(rootDirectory, "*", /*recursive*/false);
    let directoryIndex = 0;

    const minFilesPerCall = Environment.hasVariable(filesPerCredScanCall) ? Environment.getNumberValue(filesPerCredScanCall) : 500;
    const additionalCalls = Math.mod(files.length, minFilesPerCall) > 0 ? 1 : 0;
    const numCredScanCalls = Math.div(files.length, minFilesPerCall) + additionalCalls;

    for (let i = 0; i < numCredScanCalls; i++) {
        const credScanWorkingDirectory = Context.getNewOutputDirectory("credscan");
    
        // Generate a TSV file for all files to be scanned by CredScan
        const scannedFiles = i === numCredScanCalls-1 && additionalCalls === 1
            ? files.slice(minFilesPerCall * i, files.length)
            : files.slice(minFilesPerCall * i, minFilesPerCall * (i + 1));
        
        const scanPaths = scannedFiles.map(file => file.path);
        const tsvFile = Transformer.writeAllLines(p`${credScanWorkingDirectory.path}/guardian.TSV`, scanPaths);
        
        // Schedule cred scan pips
        results.add(createGuardianCall(
            guardianToolRoot,
            packageDirectory,
            guardianDrop,
            [tsvFile, ...scannedFiles],
            `credscan_${i}`,
            credScanWorkingDirectory,
            a`CredScan_${i.toString()}.sarif`,
            [guardianCredScanConfigFile],
            /*environmentVariables*/undefined,
            /*retryExitCodes*/[-9000],
            /*processRetries*/3,
            /*pathDirectories*/undefined,
            /*additionalOutputs*/undefined,
            /*untrackedPaths*/undefined,
            /*untrackedScopes*/[d`${packageDirectory.path}/nuget/Microsoft.Security.CredScan.Client.2.2.9.6-preview/lib/netcoreapp3.1/SRM`],
            /*allowUndeclaredSourceReads*/false)
        );
    }

    return results.toArray();
}

function credScanConfiguration() : Object {
    return {
        "fileVersion": "1.4",
        "tools": [
            {
                "fileVersion": "1.4",
                "tool": {
                    "name": "CredScan",
                    "version": "2.2.9.6-preview"
                },
                "arguments": {
                    "TargetDirectory": "$(WorkingDirectory)/guardian.TSV",
                    "OutputType": "pre",
                    "SuppressAsError": true,
                    "Verbose": complianceLogLevel === "Trace"
                },
                "outputExtension": "xml",
                "successfulExitCodes": [
                    0,
                    2,
                    4,
                    6
                ],
                "errorExitCodes": {
                    "1": "Partial scan completed with warnings.",
                    "3": "Partial scan completed with credential matches and warnings.",
                    "5": "Partial scan completed with application warnings and credential matches",
                    "7": "Partial scan completed with application warnings, suppressed warnings, and credential matches",
                    "-1000": "Argument Exception.",
                    "-1100": "Invalid configuration.",
                    "-1500": "Configuration Exception.",
                    "-1600": "IO Exception.",
                    "-9000": "Unexpected Exception."
                }
            }
        ]
    };
}