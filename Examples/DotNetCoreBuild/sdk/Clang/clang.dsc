// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Bash from "Bash";
import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

@@public
export const clangTool = <Transformer.ToolDefinition> {
    exe: f`/usr/bin/clang`,
    prepareTempDirectory: true,
    runtimeDependencies: [
        ...(Environment.hasVariable("HOME") ? [
            f`${Environment.getDirectoryValue("HOME")}/.CFUserTextEncoding`
        ] : [])
    ],
    untrackedDirectoryScopes: [
        ...Bash.untrackedSystemScopes,
        ...globFolders(d`/Applications`, "Xcode*.app")
    ]
};

interface Arguments {
    inputs: File[];
    outputFileName: PathAtom;
}

@@public
export function compile(args: Arguments): DerivedFile {
    const outDir = Context.getNewOutputDirectory('clang');
    const outFile = p`${outDir}/${args.outputFileName}`;
    const result = Transformer.execute({
        tool: clangTool,
        workingDirectory: outDir,
        arguments: [
            Cmd.args(args.inputs.map(Artifact.input)),
            Cmd.option("-o ", Artifact.output(outFile)),
        ],
        implicitOutputs: [
            outDir
        ]
    });
    return result.getOutputFile(outFile);
}
