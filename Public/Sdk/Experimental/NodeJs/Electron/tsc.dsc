// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";
import {Node, Npm} from "Sdk.NodeJs";
import * as Deployment from "Sdk.Deployment";

namespace TypeScript {
    /**
     * This installs the yarn packages for the given project.
     * To ensure we don't write in the source tree this will copy the project into the output folder
     */
    @@public
    export function tsc(args: Arguments) : Result {
        const outFolder = Context.getNewOutputDirectory(`tsc`);
        
        const arguments : Argument[] = [
            Cmd.argument(Artifact.none(p`${args.nodeModules.root}/typescript/bin/tsc`)),
            Cmd.option("--outDIR ", Artifact.output(outFolder)),
        ];

        const result = Node.run({
            arguments: arguments,
            workingDirectory: args.projectFolder.root,
            dependencies: [
                args.projectFolder,
                args.nodeModules,
            ],
        });

        return {
            outFolder: <OpaqueDirectory>result.getOutputDirectory(outFolder),
        };
    }

    @@public
    export interface Arguments {
        nodeModules: OpaqueDirectory,
        projectFolder: StaticContentDirectory,
    }

    @@public
    export interface Result {
        outFolder: OpaqueDirectory,
    }

}