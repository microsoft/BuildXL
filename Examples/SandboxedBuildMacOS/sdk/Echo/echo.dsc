// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import * as Bash from "Bash";

@@public
export const cmdTool: Transformer.ToolDefinition = Context.isWindowsOS()
    ? getCmdToolDefinition()
    : Bash.toolDefinition;

function getCmdToolDefinition(): Transformer.ToolDefinition {
    return {
        exe: f`${Environment.getPathValue("ComSpec")}`,
        dependsOnWindowsDirectories: true,
        untrackedDirectoryScopes: [
            d`${Environment.getPathValue("SystemRoot")}`
        ]
    };
}

@@public
export function echoViaShellExecute(message: string, printDebug?: boolean): DerivedFile {
    const wd = Context.getNewOutputDirectory("cmd");
    const outFile = p`${wd}/stdout.txt`;
    const exeArgs = <Transformer.ExecuteArguments>{
        tool: cmdTool,
        workingDirectory: wd,
        arguments: Context.isWindowsOS()
            ? getExecuteArgumentsForCmd(message, outFile)
            : getExecuteArgumentsForBash(message, outFile),
        implicitOutputs: [
            outFile
        ],
    };
    if (printDebug) {
        Debug.writeLine(` *** ${Debug.dumpData(exeArgs.tool.exe)} ${Debug.dumpArgs(exeArgs.arguments)}`);
    }
    return Transformer.execute(exeArgs).getOutputFile(outFile);
}

function getExecuteArgumentsForCmd(message: string, outFile: Path): Argument[] {
    return [
        Cmd.argument("/d"),
        Cmd.argument("/c"),
        Cmd.argument("echo"),
        Cmd.argument(message),
        Cmd.rawArgument(" > "),
        Cmd.argument(Artifact.output(outFile))
    ];
}

function getExecuteArgumentsForBash(message: string, outFile: Path): Argument[] {
    return [
        Cmd.argument("-c"),
        Cmd.argument(`echo ${message} > ${Debug.dumpData(outFile)}`)
    ];
}
