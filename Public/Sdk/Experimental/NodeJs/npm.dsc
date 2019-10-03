// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

namespace Npm {

    @@public
    export function install(args: Arguments) : Result {
        const folder = Context.getNewOutputDirectory(`npm-${args.name}`);
        const nodeModulesPath = d`${folder}/node_modules`;
        const npmCachePath = d`${folder}/npm-cache`;

        const arguments: Argument[] = [
            Cmd.argument(Artifact.input(Node.npmCli)),
            Cmd.argument("install"),
            Cmd.argument(`${args.name}@${args.version}`),
            Cmd.argument("--no-save"), // Prevents writing json files
            Cmd.argument("--no-package-lock"), // Prevents writing json files
            Cmd.argument("--no-bin-links"), // Prevents symlinks
            Cmd.option("--cache ", Artifact.none(npmCachePath)), // Forces the npm cache to use this output folder for this object so taht it doesn't write to user folder
        ];

        const result = Node.run({
            arguments: arguments,
            workingDirectory: folder,
            outputs: [
                nodeModulesPath,
                npmCachePath, // Place the cache path as an output directory so it is cleaned each time.
            ],

            environmentVariables: [
                { name: "NPM_CONFIG_USERCONFIG", value: f`${folder}/.npmrc` }, // Prevents user configuration to change behavior
                { name: "NPM_CONFIG_GLOBALCONFIG", value: f`${folder}/global.npmrc` }, // Prevent machine installed configuration file to change behavior.
                { name: "NO_UPDATE_NOTIFIER", value: "1" }, // Prevent npm from checking for the latest version online and write to the user folder with the check information
            ],
        });

        return {
            nodeModules: result.getOutputDirectory(nodeModulesPath),
        };
    }

    @@public
    export function npmInstall(rootDir: StaticDirectory): OpaqueDirectory {
        const wd = rootDir.root;
        const nodeModulesPath = d`${wd}/node_modules`;
        const npmCachePath = Context.getNewOutputDirectory('npm-install-cache');

        const arguments: Argument[] = [
            Cmd.argument(Artifact.input(Node.npmCli)),
            Cmd.argument("install"),
            Cmd.option("--cache ", Artifact.none(npmCachePath)), // Forces the npm cache to use this output folder for this object so that it doesn't write to user folder
        ];

        const result = Node.run({
            arguments: arguments,
            workingDirectory: wd,
            dependencies: [ rootDir ],
            outputs: [
                { directory: wd, kind: "shared" },
                npmCachePath, // Place the cache path as an output directory so it is cleaned each time.
            ],
            environmentVariables: [
                { name: "NPM_CONFIG_USERCONFIG", value: f`${wd}/.npmrc` }, // Prevents user configuration to change behavior
                { name: "NPM_CONFIG_GLOBALCONFIG", value: f`${wd}/global.npmrc` }, // Prevent machine installed configuration file to change behavior.
                { name: "NO_UPDATE_NOTIFIER", value: "1" }, // Prevent npm from checking for the latest version online and write to the user folder with the check information
            ],
        });

        return result.getOutputDirectory(wd);
    }

    @@public
    export interface Arguments {
        name: string,
        version: string,
    }

    export interface Result {
        nodeModules: OpaqueDirectory
    }
}