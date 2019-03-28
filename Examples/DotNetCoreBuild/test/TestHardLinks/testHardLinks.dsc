// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Bash from "Bash";
import * as Clang from "Clang";
import {Cmd, Artifact, Transformer} from "Sdk.Transformers";

const tool = !Context.isWindowsOS() && <Transformer.ToolDefinition>{
    exe: Clang.compile({
        inputs: [f`testHardLinks.c`],
        outputFileName: a`testHardLinks`
    }),
    untrackedDirectoryScopes: Bash.untrackedSystemScopes
};

export function runTool(hardlinkName: string, srcFile: File, probeFile: File): DerivedFile {
    if (Context.isWindowsOS()) return undefined;

    const outDir = Context.getNewOutputDirectory('hl');
    const hardlinkPath = p`${outDir}/${hardlinkName}`;
    const hardlinkFile = Bash.runBashCommand("mk-hardlink-" + hardlinkName, [
        Cmd.args([
            Artifact.input(f`/bin/ln`),
            "-f",
            Artifact.input(srcFile),
            Artifact.output(hardlinkPath)
        ])
    ], true).getOutputFile(hardlinkPath);

    const outFile = p`${outDir}/hl-stdout.txt`;
    const result = Transformer.execute({
        tool: tool,
        workingDirectory: outDir,
        description: "testHardlink " + hardlinkName,
        arguments: [
            Cmd.argument(Artifact.input(hardlinkFile)),
            Cmd.argument(Artifact.input(probeFile)),
            Cmd.option("--usleep ", 100), // 100 microseconds
        ],
        consoleOutput: outFile
    });

    return result.getOutputFile(outFile);
}

@@public export const pip1 = runTool("hardlink-src-1.txt", f`src-file.txt`, f`module.config.dsc`);
@@public export const pip2 = runTool("hardlink-src-2.txt", f`src-file.txt`, f`module.config.dsc`);