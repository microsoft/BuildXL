// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

import {Shared, PlatformDependentQualifier} from "Sdk.Native.Shared";

export declare const qualifier: PlatformDependentQualifier;

@@public
export interface Arguments extends Transformer.RunnerArguments {
    /**
        * Causes the preprocessor to process the named include files.
        * This corresponds the CL's /FI command-line argument.
        */
    @@Tool.option("/FI")
    forcedIncludeFiles?: File[];

    /** Prevents the compiler from searching for include files in directories specified in the PATH and INCLUDE environment variables. */
    @@Tool.option("/X")
    ignoreStandardIncludePath?: boolean;

    /**
     * This argument allows you to pass two things:
     * File: the runners can take files that are in or under the BuildXL spec file directory.
     * StaticDirectory: the runner can take a sealed directory with a bunch of includes.
     */
    @@Tool.option("/I")
    includes?: (File | StaticDirectory)[];

    /** Optional filename override. Input file name will be used if not specified. */
    outputFileName?: PathAtom;

    /**
     * Specifies a list of one or more preprocessing symbols.
     * For .def use PREPROCESSDEF
     * for .rc use RC_INVOKED
     */
    @@Tool.option("/D")
    preprocessorSymbols?: Shared.PreprocessorSymbol[];

    /** Specifies a source file. */
    source: Shared.SourceFileArtifact;

    /** Strip #line directives from the preprocessed output. */
    @@Tool.option("/EP")
    stripLineDirectives?: boolean;

    /** Suppresses the display of the sign-on banner when the compiler starts up and display of informational messages during compilation. */
    @@Tool.option("/nologo")
    suppressStartupBanner?: boolean;
}

export const defaultArgs: Arguments = {
    suppressStartupBanner: true,
    ignoreStandardIncludePath: true,
    source: undefined,
    stripLineDirectives: true,
    includes: [],
    preprocessorSymbols: []
};

/**
 * Determines if the file is a .def file
 */
@@public
export function isDefFile(source: Shared.SourceFileArtifact): boolean {
    return typeof source === "File" && (source as File).extension === a`.def`;
}

@@Tool.runner("cl -p")
@@public
export function evaluate(args: Arguments): File {
    Contract.requires(args.source !== undefined, "must provide source file");

    args = defaultArgs.merge<Arguments>(args);

    let outDir = Context.getNewOutputDirectory("c-preprocessor");

    let includes          = args.includes.mapDefined(include => typeof include === "File" ? include as File : undefined);
    let includeSearchDirs = args.includes.mapDefined(include => typeof include !== "File" ? include as StaticDirectory : undefined);

    let outFile = outDir.combine(args.outputFileName || Shared.getFile(args.source).name);

    let cmdArgs: Argument[] = [
        Cmd.flag("/nologo", args.suppressStartupBanner),
        Cmd.startUsingResponseFile(false),
        Cmd.argument("/P"),
        Cmd.flag("/EP", args.stripLineDirectives),
        Cmd.options("/FI", Artifact.inputs(args.forcedIncludeFiles)),
        Cmd.options("/D", (args.preprocessorSymbols || []).map(Shared.preprocessorSymbolToString)),
        Cmd.flag("/X", args.ignoreStandardIncludePath),
        Cmd.option("/I", Artifact.none(Context.getSpecFileDirectory()), !includes.isEmpty()),
        Cmd.options("/I", Artifact.inputs(includeSearchDirs)),
        Cmd.option("/Tc", Artifact.input(Shared.getFile(args.source))),
        Cmd.option("/Fi", Artifact.output(outFile))
    ];

    let outputs = Transformer.execute({
        tool: args.tool || importFrom("VisualCpp").clTool,
        tags: args.tags,
        workingDirectory: Context.getSpecFileDirectory(),
        dependencies: includes,
        arguments: cmdArgs,
    });

    return outputs.getOutputFile(outFile);
}
