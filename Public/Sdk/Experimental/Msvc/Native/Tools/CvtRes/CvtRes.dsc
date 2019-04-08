// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import { Artifact, Cmd, Tool, Transformer } from "Sdk.Transformers";

import {Shared} from "Sdk.Native.Shared";

import * as Link from "Sdk.Native.Tools.Link";

export function defaultTool(): Transformer.ToolDefinition {
    Contract.fail("No default tool was provided");
    return undefined;
}

/**
 * Arguments passed to the CvtResRunner
 */
// @@toolName("cvtres.exe")
@@public
export interface Arguments extends Transformer.RunnerArguments {
    /** Fold duplicate resources. */
    @@Tool.option("/FOLDDUPS")
    foldDuplicateResources?: boolean;

    /** Overrides the default name of the generated object file. */
    @@Tool.option("/OUT")
    outputFile?: PathAtom;

    /** The input .RES files */
    sources: File[];

    /** Mark data as read-only. */
    @@Tool.option("/READONLY")
    readOnlyData?: boolean;

    /** Suppress the start-up informational display as well as informational messages during compilation. */
    @@Tool.option("/nologo")
    suppressStartupBanner?: boolean;

    /** Specifies the target platform for the generated object file. */
    @@Tool.option("/MACHINE")
    targetMachine?: Link.Machine;

    /** Display progress messages */
    @@Tool.option("/VERBOSE")
    verbose?: boolean;
}

/**
 * The value produced by the CvtRes runner
 */
@@public
export interface Result {
    /** The generated .obj file */
    objectFile: File;
}

export const defaultArguments: Arguments = {
    foldDuplicateResources: false,
    readOnlyData: true,
    sources: undefined,
    suppressStartupBanner: true,
    verbose: false,
};

/**
 * Runner for the tool:CVTRES.EXE
 *
 * It converts Windows binary resource files (.RES) to Common Object File Format (COFF) object files.
 */
@@public
export function evaluate(args: Arguments) : Result {
    Contract.requires(args.sources !== undefined, "sources must not be undefined");
    Contract.requires(!args.sources.isEmpty(), "sources must not be empty");

    args = defaultArguments.override<Arguments>(args);

    const outputFileName: PathAtom = args.outputFile ? args.outputFile : args.sources[0].name.changeExtension(".obj");

    const outputDirectory = Context.getNewOutputDirectory("cvtres");
    const outputFile = outputDirectory.combine(outputFileName);
    
    // CvtRes.exe does not support response files.
    const cmdArgs: Argument[] = [
        Cmd.flag("/NOLOGO", args.suppressStartupBanner),
        Cmd.flag("/VERBOSE", args.verbose),
        Cmd.option("/MACHINE:", Shared.enumConstToUpperCase(args.targetMachine)),
        Cmd.flag("/READONLY", args.readOnlyData),
        Cmd.flag("/FOLDDUPS", args.foldDuplicateResources),

        // output artifacts
        Cmd.option("/OUT:", Artifact.output(outputFile)),

        // input files
        Cmd.args(Artifact.inputs(args.sources)),
    ];

    const result = Transformer.execute({
        tool: args.tool || defaultTool(),
        workingDirectory: outputDirectory,
        tags: args.tags,
        arguments: cmdArgs,
    });

    return <Result>{
        objectFile: result.getOutputFile(outputFile),
    };
}
