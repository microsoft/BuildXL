// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import * as Managed from "Sdk.Managed";
import * as MacOS from "Sdk.MacOS";

const pkgContents = Context.getCurrentHost().os === "win"
    ? importFrom("Bond.CSharp").Contents.all
    : importFrom("Bond.CSharp.osx-x64").Contents.all;

@@public
export const tool : Transformer.ToolDefinition = {
    exe: pkgContents.getFile(r`tools/${"gbc" + (Context.getCurrentHost().os === "win" ? ".exe" : "")}`),
    dependsOnCurrentHostOSDirectories: true,
    prepareTempDirectory: true
};

@@public
export function generate(args: Arguments) : Result {
    let outputDirectory = Context.getNewOutputDirectory("output");
    let includeDirs = (args.includeFiles || []).map(f => f.path.parent).unique();

    let arguments : Argument[] = [
        Cmd.argument("c#"),
        Cmd.option("-o ", Artifact.none(outputDirectory)),
        Cmd.options("-i ", includeDirs.map(Artifact.none)),
        Cmd.argument(Artifact.input(args.bondFile)),
    ];

    let bondFileWithoutExtension = args.bondFile.nameWithoutExtension;
    let typesFile = p`${outputDirectory}/${bondFileWithoutExtension + "_types.cs"}`;

    let result = Transformer.execute({
        tool: args.tool || tool,
        arguments: arguments,
        workingDirectory: outputDirectory,
        outputs: [
            typesFile
        ],
        dependencies: args.includeFiles
    });

    return {
        csharpResult: {
            typesFile: result.getOutputFile(typesFile),
        },
    };
}

@@public
export interface Arguments extends Transformer.RunnerArguments{
    /** Bond file to use for code generation. */
    bondFile: File;

    /** Directories to add to the list of directories used to search imported files. */
    includeFiles?: File[];
}

@@public
export interface Result {
    csharpResult?: CSharpResult;
}

@@public
export interface CSharpResult {
    typesFile: File;
}
