// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

namespace Rush {
    
    /**
     * Returns a static directory containing a valid Rush installation.
     * This function requires Rush installed locally
     */
    @@public
    export function getDefaultRushInstallation() : StaticDirectory {
        const installedRushLocation = d`${Context.getMount("LocalAppData").path}/npm`;
        const rush = f`${installedRushLocation}/rush`;
        if (!File.exists(rush))
        {
            Contract.fail(`Could not find Rush installed. File '${rush.toDiagnosticString()}' does not exist.`);
        }

        return Transformer.sealDirectory(installedRushLocation, globR(installedRushLocation));
    }

    /**
     * Returns a Transformer.ToolDefinition for Rush.
     * If the Rush installation is not provided, getDefaultRushInstallation() is used to find a locally installed one
     */
    @@public
    export function getRushTool(rushInstallation?: StaticDirectory, relativePathToInstallation?: RelativePath) : Transformer.ToolDefinition {
        return getTool(rushInstallation, getDefaultRushInstallation, () => Context.isWindowsOS? a`rush.cmd` : a`rush`, relativePathToInstallation);
    }

    /**
     * Arguments for running rush install.
     */
    @@public
    export interface RushInstallArguments extends InstallArgumentsCommon {
        rushTool: Transformer.ToolDefinition,
        repoRoot: Directory,
        absoluteSymlinks?: boolean,
        pnpmStorePath?: Directory,
        untrackedScopes?: Directory[]
    }

    /**
     * Runs Rush install as specified in the arguments.
     */
    @@public
    export function runRushInstall(arguments: RushInstallArguments) : Transformer.ExecuteResult {
        // If not specified explicitly, look for the nuget cache folder, otherwise use an arbitrary output folder
        const cacheFolder = arguments.pnpmStorePath || 
            (Environment.hasVariable("NugetMachineInstallRoot") 
                ? d`${Environment.getDirectoryValue("NugetMachineInstallRoot")}/.pnpm-store`
                : Context.getNewOutputDirectory("rushCache"));

        const localUserProfile = Context.getNewOutputDirectory("userprofile");
        
        // Rush doesn't seem to have a way to point it to the user profile .npmrc (NPM_CONFIG_USERCONFIG is not honored)
        // So let's copy the *.npmrc files from the user profile into the redirected one
        const userProfile = Environment.getDirectoryValue("USERPROFILE");
        const npmrcFiles = Transformer.sealSourceDirectory({patterns: ["*.npmrc"], include: "topDirectoryOnly", root: userProfile});
        const copiedNpmrcFiles = Transformer.copyDirectory({
            sourceDir: userProfile,
            targetDir: localUserProfile,
            dependencies: [npmrcFiles],
            pattern: "*.npmrc",
            recursive: false
        });    

        // If not specified, the default environment sets node in the path, since the basic Rush operations assume it there
        const defaultEnv : Transformer.EnvironmentVariable[] = 
        [
            {name: "PATH", separator: ";", value: [
                arguments.nodeTool.exe.parent,
                // On Windows, Rush depends on powershell being on the PATH
                ...addIf(Context.getCurrentHost().os === "win", p`${Context.getMount("Windows").path}/system32/windowspowershell/v1.0/`),
                // On Windows, Rush depends on cmd.exe being on the PATH
                ...addIf(Context.getCurrentHost().os === "win", p`${Context.getMount("Windows").path}/system32/`)]},
            {name: "RUSH_ABSOLUTE_SYMLINKS", value: arguments.absoluteSymlinks? "TRUE" : "FALSE"},
            {name: "RUSH_PNPM_STORE_PATH", value: cacheFolder},
            {name: "NO_UPDATE_NOTIFIER", value: "1"}, // Prevent npm from checking for the latest version online and write to the user folder with the check information
        ];

        // We always override USERPROFILE so we can correctly declare the output
        const overriddenEnv : Transformer.EnvironmentVariable[] =
        [
            {name: "USERPROFILE", value: localUserProfile},
        ];

        // Runtime dependencies on the node installation is not really required because of undeclared reads being on, but
        // it is better to specify those since we know them
        const additionalDependencies = [
            ...(arguments.nodeTool.runtimeDependencies || []), 
            ...(arguments.nodeTool.runtimeDirectoryDependencies || []),
            ...(arguments.additionalDependencies || []),
            copiedNpmrcFiles
            ];

        // Merge the default environment with the user provided one, if any
        // With this, the user-provided values will be set last and effectively override the defaults
        // With the same logic we will set the variables in overridenEnv last 
        const environment = defaultEnv.concat(arguments.environment || [], overriddenEnv);

        // Run rush install
        let rushInstallArgs : Transformer.ExecuteArguments = {
            tool: arguments.rushTool,
            workingDirectory: arguments.repoRoot,
            environmentVariables: environment,
            arguments:[
                Cmd.rawArgument("install"),
                Cmd.option("--max-install-attempts", arguments.processRetries),
            ],
            outputs: [
                {kind: "shared", directory: localUserProfile},
                {kind: "shared", directory: d`${arguments.repoRoot}`},
            ],
            dependencies: additionalDependencies,
            unsafe: {
                untrackedScopes: [
                    // Preserve the cache folder
                    cacheFolder,
                    // Many times there are some accesses under .git folder that are sensitive to file content that introduce
                    // unwanted cache misses
                    d`${arguments.repoRoot}/.git`,
                    ...(arguments.untrackedScopes || [])
                ],
                untrackedPaths: [
                    // Changes in the user profile .npmrc shouldn't induce a cache miss
                    f`${localUserProfile}/.npmrc`,
                    f`${localUserProfile}/global.npmrc`,
                ],
                passThroughEnvironmentVariables: defaultPassthroughVariables,
            },
        };

        return Transformer.execute(Object.merge(defaults, rushInstallArgs));
    }
}