// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

const dotnetExeVarName = "DOTNET_EXE";
const dotnetExecutable = Environment.hasVariable(dotnetExeVarName)
    ? Environment.getFileValue(dotnetExeVarName)
    : f`/usr/local/bin/dotnet`;

@@public
export const dotnetTool: Transformer.ToolDefinition = {
    exe: dotnetExecutable,
    dependsOnWindowsDirectories: true,
    dependsOnAppDataDirectory: true,
    prepareTempDirectory: true,
    untrackedDirectoryScopes: [
        d`${dotnetExecutable.parent}`,
        ...addIfLazy(Context.getCurrentHost().os !== "win", () => [
            d`${Environment.getDirectoryValue("HOME")}/.dotnet`,
            d`/usr/local/share/dotnet`,
            d`/etc`
        ]),
    ]
};

@@public
export interface RuntimeOptions {
    /** Hint for the name of the output directory. */
    outDirHint?: string;

    /** Paths containing probing policy and assemblies to probe for. */
    additionalProbingPaths?: File[];

    /** Version of the installed Shared Framework to use to run the application. */
    fxVersion?: string;

    /** Roll forward on no candidate shared framework is enabled. */
    rollForwardOnNoCandidateFx?: boolean;

    /** Paths to additional deps.json files. */
    additionalDeps?: File[];
}

@@public
export interface Arguments {
    pathToApplication: File;
    applicationArguments: Argument[];
    runtimeOptions?: RuntimeOptions;
    executeArgsTemplate?: Transformer.ExecuteArguments;
    executeArgsOverride?: Transformer.ExecuteArguments;
}

@@public
export function execute(args: Arguments): Transformer.ExecuteResult {
    Contract.requires(args !== undefined);
    Contract.requires(args.pathToApplication !== undefined);

    const runtimeOptions = args.runtimeOptions || {};
    const executeTemplate = args.executeArgsTemplate || {};
    const outDir = Context.getNewOutputDirectory(runtimeOptions.outDirHint || "dotnet");
    const myExeArgs = <Transformer.ExecuteArguments>{
        tool: dotnetTool,
        workingDirectory: d`${args.pathToApplication.parent}`,
        consoleOutput: p`${outDir}/stdout.txt`,
        consoleError: p`${outDir}/stderr.txt`,
        arguments: [
            Cmd.options("--additionalprobingpath ", (runtimeOptions.additionalProbingPaths || []).map(Artifact.input)),
            Cmd.options("--additional-deps ", (runtimeOptions.additionalDeps || []).map(Artifact.input)),
            Cmd.option("--fx-version ", runtimeOptions.fxVersion),
            Cmd.flag("--roll-forward-on-no-candidate-fx", runtimeOptions.rollForwardOnNoCandidateFx),
            Cmd.argument(Artifact.input(args.pathToApplication)),
            ...(args.applicationArguments || [])
        ]
    };

    const finalExeArgs = merge<Transformer.ExecuteArguments>(args.executeArgsTemplate, myExeArgs, args.executeArgsOverride);
    return Transformer.execute(finalExeArgs);
}

function merge<T>(base: T, main: T, override: T): T {
    return (base || {}).override<T>(main || {}).override<T>(override || {});
}
