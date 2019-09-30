// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

namespace Npm {

    function _install(workingDirectory: StaticDirectory, command: string, ...cmdArgs: Argument[]) : Result {
        const wd = workingDirectory !== undefined
            ? workingDirectory.root
            : Context.getNewOutputDirectory(`npm-${command}`);
        const nodeModulesPath = d`${wd}/node_modules`;
        const npmCachePath = Context.getNewOutputDirectory('npm-install-cache');

        const arguments: Argument[] = [
            Cmd.argument(Artifact.input(Node.npmCli)),
            Cmd.argument("install"),
            ...cmdArgs,
            Cmd.argument("--no-save"), // Prevents writing json files
            Cmd.argument("--no-package-lock"), // Prevents writing json files
            Cmd.argument("--no-bin-links"), // Prevents symlinks
            Cmd.option("--cache ", Artifact.none(npmCachePath)), // Forces the npm cache to use this output folder for this object so that it doesn't write to user folder
        ];

        const result = Node.run({
            arguments: arguments,
            workingDirectory: wd,
            dependencies: [ workingDirectory ].filter(x => x !== undefined),
            outputs: [
                nodeModulesPath,
                npmCachePath, // Place the cache path as an output directory so it is cleaned each time.
            ],

            environmentVariables: [
                { name: "NPM_CONFIG_USERCONFIG", value: f`${wd}/.npmrc` }, // Prevents user configuration to change behavior
                { name: "NPM_CONFIG_GLOBALCONFIG", value: f`${wd}/global.npmrc` }, // Prevent machine installed configuration file to change behavior.
                { name: "NO_UPDATE_NOTIFIER", value: "1" }, // Prevent npm from checking for the latest version online and write to the user folder with the check information
            ],
        });

        return {
            nodeModules: result.getOutputDirectory(nodeModulesPath),
        };
    }

    @@public
    export function install(args: Arguments) : Result {
        return _install(undefined, "install", Cmd.argument(`${args.name}@${args.version}`));
    }

    @@public
    export function installFromPackageJson(workingStaticDirectory : StaticDirectory) : Result {
        return _install(workingStaticDirectory, "install");
    }

    @@public
    export function runCompile(workingStaticDirectory: StaticDirectory, nodeModulesDir: StaticDirectory) : OpaqueDirectory {
        const wd = workingStaticDirectory.root;
        const outPath = d`${wd}/out`;
        const npmCachePath = Context.getNewOutputDirectory('npm-compile-cache');
        const arguments: Argument[] = [
            Cmd.argument(Artifact.none(f`${wd}/node_modules/typescript/lib/tsc.js`)),
            Cmd.argument("-p"),
            Cmd.argument("."),
        ];

        const result = Node.run({
            arguments: arguments,
            workingDirectory: wd,
            dependencies : [workingStaticDirectory, nodeModulesDir],
            outputs: [
                outPath,
                npmCachePath
            ]
        });

        return result.getOutputDirectory(outPath);
    }

    @@public
    export interface Arguments {
        name: string,
        version: string,
    }

    export interface Result {
        nodeModules: OpaqueDirectory,
    }
}