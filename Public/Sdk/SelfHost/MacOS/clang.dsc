// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

namespace Clang {
    @@public
    export const clangTool = <Transformer.ToolDefinition> {
        exe: f`/usr/bin/clang`,
        prepareTempDirectory: true,
        dependsOnCurrentHostOSDirectories: true
    };

    @@public
    export interface Arguments {
        /** Input file(s) to compile */
        @@Tool.option('')
        inputs: File[];

        /** Name of the output file */
        @Tool.option('-o')
        out: string | PathAtom;

        /** Generate source-level debug information */
        @Tool.option('-g')
        emitDebugInformation?: boolean;

        /** Pass the comma separated arguments in <arg> to the linker */
        @Tool.option('-Wl,')
        linkerArgs?: string[];

        /** Libraries to link with */
        @Tool.option('-l')
        libraries?: string[];

        /** macOS frameworks */
        @Tool.option('-framework')
        frameworks?: string[];
    }

    @@public
    export function compile(args: Arguments): DerivedFile {
        const outDir = Context.getNewOutputDirectory('clang');
        const outFile = p`${outDir}/${args.out}`;
        const cmdArgs: Argument[] = [
            Cmd.option("-o ", Artifact.output(outFile)),
            Cmd.args(args.inputs.map(Artifact.input)),
            Cmd.options("-l", args.libraries || []),
            Cmd.options("-framework ", args.frameworks || []),
            Cmd.option( "-Wl,", (args.linkerArgs || []).join(",")),
            Cmd.flag("-g", args.emitDebugInformation),
        ];

        const result = Transformer.execute({
            tool: clangTool,
            workingDirectory: outDir,
            arguments: cmdArgs,
            implicitOutputs: [
                outDir
            ]
        });
        return result.getOutputFile(outFile);
    }
}