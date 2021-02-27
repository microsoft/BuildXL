// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer, Cmd, Artifact} from "Sdk.Transformers";

namespace Yarn {
    /**
     * Returns a static directory containing a valid Yarn installation.
     * This function requires Yarn installed locally
     */
    @@public
    export function getDefaultYarnInstallation() : StaticDirectory {
        const installedYarnLocation = d`${Context.getMount("ProgramFilesX86").path}/Yarn`;
        const packageJson = f`${installedYarnLocation}/package.json`;
        if (!File.exists(packageJson))
        {
            Contract.fail(`Could not find Yarn installed. File '${packageJson.toDiagnosticString()}' does not exist.`);
        }

        return Transformer.sealDirectory(installedYarnLocation, globR(installedYarnLocation));
    }

    /**
     * Returns a Transformer.ToolDefinition for Yarn.
     * If the Yarn installation is not provided, getDefaultYarnInstallation() is used to find a locally installed one
     * If the relative path to Yarn is not provided, the first occurrence of Yarn in the installation is used
     */
    @@public
    export function getYarnTool(yarnInstallation?: StaticDirectory, relativePathToInstallation?: RelativePath) : Transformer.ToolDefinition {
        return getTool(yarnInstallation, getDefaultYarnInstallation, () => Context.isWindowsOS? a`yarn.cmd` : a`yarn`, relativePathToInstallation);
    }

    /**
     * Arguments for running yarn install.
     * Required arguments are the yarn and node tools to use (yarn depends on node) and the root of the repo where to run install
     */
    @@public
    export interface YarnInstallArguments extends InstallArgumentsCommon {
        yarnTool: Transformer.ToolDefinition,
        repoRoot: Directory,
        yarnCacheFolder?: Directory,
        frozenLockfile?: boolean,
        userNpmrcLocation?: NpmrcLocation,
        globalNpmrcLocation?: NpmrcLocation,
    }

    /**
     * Runs Yarn install as specified in the arguments.
     */
    @@public
    export function runYarnInstall(arguments: YarnInstallArguments) : Transformer.ExecuteResult {
        // If not specified explicitly, look for the nuget cache folder, otherwise use an arbitrary output folder
        const cacheFolder = arguments.yarnCacheFolder || 
            (Environment.hasVariable("NugetMachineInstallRoot") 
                ? d`${Environment.getDirectoryValue("NugetMachineInstallRoot")}/.yarn-cache`
                : Context.getNewOutputDirectory("yarnCache"));

        const localUserProfile = Context.getNewOutputDirectory("userprofile");

        let npmrc = resolveNpmrc(arguments.userNpmrcLocation, arguments.repoRoot, a`.npmrc`);
        let globalNpmrc = resolveNpmrc(arguments.globalNpmrcLocation, arguments.repoRoot, a`global.npmrc`);

        // If not specified, the default environment sets node in the path, since the basic Yarn operations assume it there
        const defaultEnv : Transformer.EnvironmentVariable[] = [
            {name: "PATH", separator: ";", value: [arguments.nodeTool.exe.parent]},
            {name: "NO_UPDATE_NOTIFIER", value: "1"}, // Prevent npm from checking for the latest version online and write to the user folder with the check information
            {name: "NPM_CONFIG_USERCONFIG", value: npmrc }, 
            {name: "NPM_CONFIG_GLOBALCONFIG", value: globalNpmrc },
            {name: "USERPROFILE", value: localUserProfile},
        ];

        // Runtime dependencies on the node installation is not really required because of undeclared reads being on, but
        // it is better to specify those since we know them
        const additionalDependencies = [
            ...(arguments.nodeTool.runtimeDependencies || []), 
            ...(arguments.nodeTool.runtimeDirectoryDependencies || []),
            ...(arguments.additionalDependencies || [])
            ];

        // Set the Yarn cache
        let setCacheArgs : Transformer.ExecuteArguments = {
            tool: arguments.yarnTool,
            workingDirectory: arguments.repoRoot,
            environmentVariables: arguments.environment || defaultEnv,
            arguments: [
                Cmd.rawArgument("config set cache-folder"),
                Cmd.args([Artifact.none(cacheFolder)])
            ],
            outputs: [
                p`${localUserProfile}/.yarnrc`,
            ],
            dependencies: additionalDependencies,
            unsafe: {
                untrackedPaths: [
                    npmrc,
                    globalNpmrc,
                ]
            }
        };

        let setCacheResult = Transformer.execute(Object.merge(defaults, setCacheArgs));

        // Run yarn install
        let yarnInstallArgs : Transformer.ExecuteArguments = {
            tool: arguments.yarnTool,
            workingDirectory: arguments.repoRoot,
            environmentVariables: arguments.environment || defaultEnv,
            arguments:[
                Cmd.rawArgument("install"),
                Cmd.flag("--frozen-lockfile", arguments.frozenLockfile || false)
            ],
            outputs: [
                {kind: "shared", directory: d`${arguments.repoRoot}`},
            ],
            dependencies: [
                ...setCacheResult.getOutputFiles(), 
                ...additionalDependencies],
            unsafe: {
                untrackedScopes: [
                    cacheFolder,
                    // Many times there are some accesses under .git folder that are sensitive to file content that introduce
                    // unwanted cache misses
                    d`${arguments.repoRoot}/.git`,
                ],
                untrackedPaths: [
                    npmrc,
                    globalNpmrc
                ],
                passThroughEnvironmentVariables: defaultPassthroughVariables,
            },
            processRetries: arguments.retries,
            // Yarn install fails with exit code 1, which usually means some flaky network error that can be retried
            retryExitCodes: [1],
        };

        return Transformer.execute(Object.merge(defaults, yarnInstallArgs));
    }
}