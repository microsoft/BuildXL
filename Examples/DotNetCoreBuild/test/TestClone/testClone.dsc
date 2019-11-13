// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Bash from "Bash";
import * as Clang from "Clang";
import {Cmd, Artifact, Transformer} from "Sdk.Transformers";

const cloneTool = Bash.isMacOS && <Transformer.ToolDefinition>{
    exe: Clang.compile({
        inputs: [f`testClone.c`],
        outputFileName: a`testClone`
    }),
    untrackedDirectoryScopes: Bash.untrackedSystemScopes
};

interface Arguments {
    input: File;
    outputName: PathAtom;
}

export function runClone(args: Arguments): DerivedFile {
    if (!Bash.isMacOS) return undefined;

    const outDir = Context.getNewOutputDirectory('clone');
    const outFile = p`${outDir}/${args.outputName}`;
    const result = Transformer.execute({
        tool: cloneTool,
        workingDirectory: outDir,
        arguments: [
            Cmd.argument(Artifact.input(args.input)),
            Cmd.argument(Artifact.output(outFile)),
        ],
        consoleOutput: p`${outDir}/clone-stdout.txt`,
    });
    return result.getOutputFile(outFile);
}

@@public export const testClone = runClone({
    input: f`testClone.c`,
    outputName: a`testClone.c~clone`
});
