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
    export function installFromPackageJson(workingDirectory : Directory) : OpaqueDirectory {
        const nodeModulesPath = d`${workingDirectory}/node_modules`;
        Debug.writeLine("Working files Inside: \n" + globR(workingDirectory));
        const arguments: Argument[] = [
            Cmd.argument(Artifact.input(Node.npmCli)),
            Cmd.argument("install")
        ];

        const result = Node.run({
            arguments: arguments,
            workingDirectory: workingDirectory,
            outputs: [
                {directory: nodeModulesPath, kind: "shared"}
            ]
        });
        
        Debug.writeLine("opfile: " + result.getOutputFiles().length);
        Debug.writeLine("Inside nodeModulesPath: " + result.getOutputDirectory(nodeModulesPath).getContent().length);

        return result.getOutputDirectory(nodeModulesPath);
    }

    @@public
    export function runCompile(workingDirectory : Directory) : void {
        const outPath = d`${workingDirectory}/out`;
        const arguments: Argument[] = [
            Cmd.argument(Artifact.input(Node.npmCli)),
            Cmd.argument("run"),
            Cmd.argument("compile")
        ];

        const result = Node.run({
            arguments: arguments,
            workingDirectory: workingDirectory,
            outputs: [
                {directory: outPath, kind: "shared"}
            ]
        });
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
