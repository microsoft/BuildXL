// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

namespace RcEdit {
    export function setIcon(args: Arguments) : Result {

        // TODO: There are two features missing why we have to wrap rcedit in cmd.
        // The first is that we lack the ability to extract a file from an opaque directory
        // The second is tha this tool writes a temp file next to the exe it rewrites
        // Since we lack an untracked files with wildcards we are forced to wrap this tool in cmd
        // to let it run in a temp (hack) folder and then copy the file to the final output in cmd.

        const hackFolder = Context.getNewOutputDirectory("hack");
        const hackFile = p`${hackFolder}/${args.file.name}`;

        const outFolder = Context.getNewOutputDirectory("rcedit");
        const outFile = p`${outFolder}/${args.file.name}`;

        const arguments : Argument[] = [
            Cmd.argument("/D"), // disable autorun
            Cmd.argument("/C"), // execute and then terminate
            Cmd.argument(Artifact.none(p`${args.nodeModules.root}/rcedit/bin/rcedit.exe`)),
            Cmd.argument(Artifact.rewritten(args.file, hackFile)),
            Cmd.option("--set-icon ", Artifact.input(args.icon)),
            Cmd.argument("&&"),
            Cmd.argument("copy"),
            Cmd.argument(Artifact.none(hackFile)),
            Cmd.argument(Artifact.output(outFile)),
        ];

        const result = Transformer.execute({
            tool: { 
                // TODO: We don't have a supported way to get files out of opaque directories, so there is no 
                // way to get a file handle to an executable in the modules folder. hence we have to 
                // wrap it is cmd.exe
                exe: f`${Environment.getPathValue("ComSpec")}`,
                dependsOnWindowsDirectories: true,
            },
            arguments: arguments,
            workingDirectory: d`${args.file.parent}`,
            dependencies: [
                args.nodeModules,
            ],
            unsafe: {
                untrackedScopes: [
                hackFolder // we have to hack because BuildXL doesn't support untracked files with wildcards yet.
                ],
            },
        });

        return {
            file: result.getOutputFile(outFile),
        };
    }

    export interface Arguments {
        file: File,
        icon: File,
        nodeModules: OpaqueDirectory,
    }

    export interface Result {
        file: File,
    }

}
