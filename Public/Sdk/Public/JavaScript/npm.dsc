// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

namespace Npm {

    /**
     * Arguments for running npm install.
     */
    @@public
    export interface NpmInstallArguments extends InstallArgumentsCommon {
        npmTool: Transformer.ToolDefinition,
        targetFolder: Directory,
        package?: {name: string, version: string},
        npmCacheFolder?: Directory,
        preserveCacheFolder?: boolean,
        noBinLinks?: boolean,
        userNpmrcLocation?: NpmrcLocation,
        globalNpmrcLocation?: NpmrcLocation,
        additionalArguments?: Argument[],
    }

    /**
     * Returns a static directory containing a valid Npm installation.
     * This function requires Npm installed locally
     */
    @@public
    export function getDefaultNpmInstallation() : StaticDirectory {
        const installedNpmLocation = d`${Context.getMount("ProgramFiles").path}/nodejs`;
        const npm = f`${installedNpmLocation}/${npmTool()}`;
        if (!File.exists(npm))
        {
            Contract.fail(`Could not find Npm installed. File '${npm.toDiagnosticString()}' does not exist.`);
        }

        return Transformer.sealDirectory(installedNpmLocation, globR(installedNpmLocation));
    }

    /**
     * Returns a Transformer.ToolDefinition for Npm.
     * If the Npm installation is not provided, getDefaultNpmInstallation() is used to find a locally installed one
     * If the relative path to Npm is not provided, the first occurrence of Npm in the installation is used
     */
    @@public
    export function getNpmTool(installation: StaticDirectory, relativePathToInstallation?: RelativePath) : Transformer.ToolDefinition {
        return getTool(installation, getDefaultNpmInstallation, npmTool, relativePathToInstallation);
    }

    function npmTool() : PathAtom { return Context.isWindowsOS? a`npm.cmd` : a`npm`; }

    /**
     * Runs Npm install as specified in the arguments.
     */
    @@public
    export function runNpmInstall(args: NpmInstallArguments) : SharedOpaqueDirectory {
        const result = runNpmInstallWithAdditionalOutputs(args, []);
        return <SharedOpaqueDirectory> result.getOutputDirectory(args.targetFolder);
    }

     /**
     * Runs Npm install as specified in the arguments with additional outputs.
     */
    @@public
    export function runNpmInstallWithAdditionalOutputs(args: NpmInstallArguments, additionalOutputs: Directory[] ) : TransformerExecuteResult {

        const preserveCacheFolder = args.preserveCacheFolder || false;

        // If not specified explicitly, look for the nuget cache folder, otherwise use an arbitrary output folder
        const npmCachePath = args.npmCacheFolder || 
                             (preserveCacheFolder && Environment.hasVariable("NugetMachineInstallRoot") 
                             ? d`${Environment.getDirectoryValue("NugetMachineInstallRoot")}/.npm-cache`
                             : Context.getNewOutputDirectory("npm-install-cache"));

        const package = args.package !== undefined ? `${args.package.name}@${args.package.version}` : undefined;

        let npmrc = resolveNpmrc(args.userNpmrcLocation, args.targetFolder, a`.npmrc`);
        let globalNpmrc = resolveNpmrc(args.globalNpmrcLocation, args.targetFolder, a`global.npmrc`);

        const arguments: Argument[] = [
            ...(args.additionalArguments || []),
            Cmd.argument("install"),
            Cmd.argument(package),
            Cmd.option("--userconfig ", Artifact.none(npmrc)),
            Cmd.option("--globalconfig ", Artifact.none(globalNpmrc)),
            Cmd.argument("--no-save"), // Prevents writing json files
            Cmd.flag("--no-bin-links", args.noBinLinks), // Prevents symlinks
            Cmd.option("--cache ", Artifact.none(npmCachePath)), // Forces the npm cache to use this output folder for this object so that it doesn't write to user folder
        ];

        // Redirect the user profile to an output directory to avoid npm from polluting the
        // real user profile folder
        const localUserProfile = Context.getNewOutputDirectory("userprofile");

        const environment = args.environment || [
            {name: "PATH", separator: ";", value: [args.nodeTool.exe.parent]},
            {name: "USERPROFILE", value: localUserProfile},
            {name: "NO_UPDATE_NOTIFIER", value: "1"}, // Prevent npm from checking for the latest version online and write to the user folder with the check information
        ];

        const npmInstallArgs : Transformer.ExecuteArguments ={
            tool: args.npmTool,
            arguments: arguments,
            workingDirectory: args.targetFolder,
            outputs: [
                {kind: "shared", directory: args.targetFolder},
                {kind: "shared", directory: localUserProfile},
                ...(additionalOutputs.map(dir => {return <Transformer.DirectoryOutput>{kind: "shared", directory: dir};})),
                ...addIf(!preserveCacheFolder, <Transformer.DirectoryOutput>{directory: npmCachePath, kind: "shared"})
            ],
            environmentVariables: environment,
            dependencies: args.additionalDependencies,
            unsafe: {
                // if we preserve the cache folder, just untrack it
                untrackedScopes: [...addIf(preserveCacheFolder, npmCachePath)],
                // avoid making install sensitive to the npmrc files since that can be machine dependent
                untrackedPaths: [npmrc, globalNpmrc],
                passThroughEnvironmentVariables: defaultPassthroughVariables,
            },
            // Npm install fails with exit code 1, which usually means some flaky network error that can be retried
            retryExitCodes: [1],
        };

        const result = Transformer.execute(Object.merge(defaults, npmInstallArgs));
        return result;
    } 
}