// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Echo from "Echo";
import * as Bash from "Bash";
import {Cmd, Artifact, Transformer} from "Sdk.Transformers";

@@public
export const foo = printFromBuildXL("HelloWorld");

const helloWorldMsgVarName = "HELLO_WORLD_MSG";

@@public
export const writeFilePipOutput = (() => {
    const outDir = Context.getNewOutputDirectory("write-pip");
    const outFile = Transformer.writeAllText({
        outputPath: p`${outDir}/write-pip-out-file.txt`,
        text: "Produced by a WriteFile pip"
    });
    Debug.writeLine(` *** Using WriteFile pip to write to ${outFile}`);
    return outFile;
})();

@@public
export const scriptWithChildProcesses = (() => {
    if (!Bash.isMacOS) return undefined;

    const outDir = Context.getNewOutputDirectory("script");
    const outFile = outDir.combine("script-out.txt");
    const args = <Transformer.ExecuteArguments>{
        tool: Bash.toolDefinition,
        workingDirectory: outDir,
        arguments: [
            Cmd.argument("-c"),
            Cmd.rawArgument('"('), // () creates a subshell, i.e., child process
            Cmd.argument(Artifact.input(f`/usr/bin/touch`)),
            Cmd.argument(Artifact.output(outFile)),
            Cmd.rawArgument(')"')
        ],
        unsafe: {
            passThroughEnvironmentVariables: [
                "PATH",
                "HOME",
                "USER"
            ]
        }
    };
    const result = Transformer.execute(args);
    const derivedFile = result.getOutputFile(outFile);
    Debug.writeLine(` *** ${Debug.dumpData(args.tool.exe)} ${Debug.dumpArgs(args.arguments)}`);
    return derivedFile;
})();

@@public
export const boo = printToFileViaPip(Environment.hasVariable(helloWorldMsgVarName)
    ? Environment.getStringValue(helloWorldMsgVarName)
    : "HelloWorldViaPip");

@@public
export const cp = (() => {
    if (!Bash.isMacOS) return undefined;

    const outDir = Context.getNewOutputDirectory("dest");
    const outFile = outDir.combine("newfile.txt");
    const args = <Transformer.ExecuteArguments>{
        tool: <Transformer.ToolDefinition>{
            exe: f`/bin/cp`,
            runtimeDependencies: [
                f`/bin/sh`
            ]
        },
        workingDirectory: outDir,
        arguments: [
            Cmd.argument(Artifact.input(f`hello-world.dsc`)),
            Cmd.argument(Artifact.output(outFile))
        ],
        unsafe: {
            untrackedScopes: Bash.untrackedSystemScopes
        }
    };
    const result = Transformer.execute(args);
    const derivedFile = result.getOutputFile(outFile);
    Debug.writeLine(` *** ${Debug.dumpData(args.tool.exe)} ${Debug.dumpArgs(args.arguments)}`);
    return derivedFile;
})();

function printFromBuildXL(message: string): number {
    Debug.writeLine(` *** ${message} from BuildXL *** `);
    return 42;
}

function printToFileViaPip(message: string): DerivedFile {
    const outFile = Echo.echoViaShellExecute(message, /* printDebug: */ true);
    return outFile;
}
