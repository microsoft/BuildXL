// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

@@public
export const isMacOS = Context.getCurrentHost().os === "macOS";

@@public
export const untrackedSystemScopes = [
    d`/usr`,
    d`/private`,
    d`/dev`,
    d`/etc`,
    d`/Library`,
    d`/System/Library`,
    d`/AppleInternal`,
    d`/var`,
    d`/bin`
];

@@public
export const toolDefinition = <Transformer.ToolDefinition>{
    exe: f`/bin/bash`,
    runtimeDependencies: [
        f`/bin/sh`
    ],
    untrackedDirectoryScopes: untrackedSystemScopes
};

@@public
export function runBashCommand(hint: string, bashArguments: Argument[], printDebug?: boolean): Transformer.ExecuteResult {
    if (!isMacOS) return undefined;

    const outDir = Context.getNewOutputDirectory(hint);
    const outFile = outDir.combine(hint + "-stdout.txt");
    const exeArgs = <Transformer.ExecuteArguments>{
        tool: toolDefinition,
        workingDirectory: outDir,
        arguments: [
            Cmd.argument("-c"),
            Cmd.rawArgument('"'),
            ...bashArguments,
            Cmd.rawArgument('"')
        ],
        consoleOutput: p`${outFile}`,
        unsafe: {
            passThroughEnvironmentVariables: [
                "PATH",
                "HOME",
                "USER"
            ]
        },
        description: `bash-${hint}`
    };
    const result = Transformer.execute(exeArgs);
    if (printDebug) {
        //Debug.writeLine(` *** ${Debug.dumpData(exeArgs.tool.exe)} ${Debug.dumpArgs(exeArgs.arguments)}`);
    }
    return result;
}

@@public
export function runInBashSubshells(hint: string, numRepeats: number, programCmdLine: ArgumentValue[], printDebug?: boolean): Transformer.ExecuteResult {
    const bashArguments = [
        Cmd.rawArgument('('), // () creates a subshell, i.e., child process
        ...join(
            Cmd.rawArgument(") ; ("),
            programCmdLine.map(Cmd.argument),
            numRepeats),
        Cmd.rawArgument(')')
    ];

    return runBashCommand(hint, bashArguments);
}

function join<T>(sep: T, arr: T[], nTimes: number): T[] {
    Contract.requires(nTimes >= 1);
    return nTimes === 1 ? arr : [
        ...join(sep, arr, nTimes - 1),
        sep,
        ...arr
    ];
}
