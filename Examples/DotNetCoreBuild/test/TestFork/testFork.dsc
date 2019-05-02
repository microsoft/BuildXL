// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Bash from "Bash";
import * as Clang from "Clang";
import {Cmd, Artifact, Transformer} from "Sdk.Transformers";

const forkTool = Bash.isMacOS && <Transformer.ToolDefinition>{
    exe: Clang.compile({
        inputs: [f`testFork.c`],
        outputFileName: a`testFork`
    }),
    untrackedDirectoryScopes: Bash.untrackedSystemScopes
};

interface Arguments {
    waitForChild: boolean;
    depth: number;
    sleep: number;
    allowSurvivors: boolean;
}

export function runFork(args: Arguments): DerivedFile {
    if (!Bash.isMacOS) return undefined;

    const outDir = Context.getNewOutputDirectory('fork');
    const outFile = p`${outDir}/fork-out.txt`;
    const result = Transformer.execute({
        tool: forkTool,
        workingDirectory: outDir,
        arguments: [
            Cmd.argument(Artifact.none(outFile)),
            Cmd.flag("--wait-for-child", args.waitForChild),
            Cmd.option("--depth ", args.depth),
            Cmd.option("--sleep ", args.sleep)
        ],
        consoleOutput: p`${outDir}/fork-stdout.txt`,
        implicitOutputs: args.allowSurvivors ? [] : [ outFile ],
        allowedSurvivingChildProcessNames: args.allowSurvivors
            ? [ forkTool.exe.name ]
            : []
    });
    return result.getOutputFile(outFile);
}

// If the file write from the inner-most process is not detected and reported to BuildXL,
// BuildXL will fail with "A pip produced outputs with no file access message."
@@public export const forkOutput1 = runFork({waitForChild: true, depth: 5, sleep: 1, allowSurvivors: false});
@@public export const forkOutput2 = runFork({waitForChild: false, depth: 5, sleep: 1, allowSurvivors: false});
@@public export const forkOutput3 = runFork({waitForChild: false, depth: 0, sleep: 100000, allowSurvivors: true});
