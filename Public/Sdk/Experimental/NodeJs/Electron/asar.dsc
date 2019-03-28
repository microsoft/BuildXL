// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";
import {Node, Npm} from "Sdk.NodeJs";
import * as Deployment from "Sdk.Deployment";

namespace Asar {

    /**
     * This installs the yarn packages for the given project.
     * To ensure we don't write in the source tree this will copy the project into the output folder
     */
    @@public
    export function pack(args: Arguments) : Result {
        const packFile = p`${Context.getNewOutputDirectory(`asar`)}/${args.name}`;
        
        const arguments : Argument[] = [
            Cmd.argument(Artifact.none(p`${args.nodeModules.root}/asar/bin/asar.js`)),
            Cmd.argument("pack"),
            Cmd.argument(Artifact.input(args.folderToPack)),
            Cmd.argument(Artifact.output(packFile)),
        ];

        const result = Node.run({
            arguments: arguments,
            workingDirectory: args.folderToPack.root,
            dependencies: [
                args.nodeModules,
            ],
        });

        return {
            packFile: result.getOutputFile(packFile),
        };
    }

    @@public
    export interface Arguments {
        name: string | PathAtom,
        folderToPack: OpaqueDirectory,
        nodeModules: OpaqueDirectory,
    }

    @@public
    export interface Result {
        packFile: File,
    }

}