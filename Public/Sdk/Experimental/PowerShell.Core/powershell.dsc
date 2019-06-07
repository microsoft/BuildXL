// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

@@public
export const tool = getPowerShellTool();

/**
 * Helper method to invoke some powershell commands
 */
@@public
export function executeCommands(commands:string[], executeArgs: Transformer.ExecuteArgumentsComposible) : Transformer.ExecuteResult {
    const script = String.join(";", [
        "Microsoft.PowerShell.Core\\Set-StrictMode -Version Latest", // Use strict syntax
        "$progressPreference='silentlyContinue'", // Prevent powershell from painting the progress bars
        "$ErrorActionPreference='Stop'", // Ensure the script stops immediagely on the first error

        ...commands,
        ]);

    const arguments: Argument[] = [
        Cmd.argument("-NonInteractive"), // Makes sure there are no user-prompt in the build
        Cmd.argument("-NoProfile"), // Prevent any user profile stuff from loading
        Cmd.option("-Command ", "& {" + script + "}"),
    ];

   const moduleAnalysisCache = Context.getTempDirectory("ModuleAnalysisCache");

   const powerShellUserLocation = Context.getCurrentHost().os === "win" 
    ? d`${Context.getMount("LocalAppData").path}/Microsoft/Windows/PowerShell`
    : d`${Context.getMount("UserProfile").path}/.cache/powershell`;


    return Transformer.execute(
        Object.merge<Transformer.ExecuteArguments>({
            tool: tool,
            workingDirectory: d`.`,
            arguments: arguments,
            environmentVariables: [
                { name: "PSModuleAnalysisCachePath", value: moduleAnalysisCache },
            ],
            additionalTempDirectories: [
                moduleAnalysisCache,
            ],
            unsafe: {
                untrackedScopes: [
                    // Powershell core stores the jit cache here which should have no effect on the output of the tools.
                    d`${powerShellUserLocation}/StartupProfileData-NonInteractive`, 
                ],
            },
        },
        executeArgs)
    );
}

function getPowerShellTool() : Transformer.ToolDefinition {
    const host = Context.getCurrentHost();

    Contract.assert(host.cpuArchitecture === "x64", "The current PowerShell.Core package only has x64 version of Node. Ensure this runs on a 64-bit OS -or- update PowerShell.Core package to have other architectures embedded and fix this logic");

    let executable : RelativePath = undefined;
    let pkgContents : StaticDirectory = undefined;
    let untrackedScopes : Directory[] = [];
    
    switch (host.os) {
        case "win":
            pkgContents = importFrom("PowerShell.Core.win-x64").extracted;
            executable = r`pwsh.exe`;
            // pwsh on windows is not respecting /noprofile and still reading files from the user folder
            untrackedScopes = [
                d`${Context.getMount("LocalLow").path}/Microsoft/CryptnetFlushCache`,
                d`${Context.getMount("LocalLow").path}/Microsoft/CryptnetUrlCache`,
            ];
            break;
        case "macOS": 
            pkgContents = importFrom("PowerShell.Core.osx-x64").extracted;
            executable = r`pwsh`;
            break;
        case "unix":
            pkgContents = importFrom("PowerShell.Core.linux-x64").extracted;
            executable = r`pwsh`;
            break;
        default:
            Contract.fail(`The current PowerShell.Core package doesn't support the current OS: ${host.os}. Esure you run on a supported OS -or- update the PowerShell.Core package to have the version embdded.`);
    }

    return {
        exe: pkgContents.getFile(executable),
        runtimeDirectoryDependencies: [
            pkgContents,
        ],
        prepareTempDirectory: true,
        dependsOnWindowsDirectories: true,
        untrackedDirectoryScopes: untrackedScopes,
    };
}
