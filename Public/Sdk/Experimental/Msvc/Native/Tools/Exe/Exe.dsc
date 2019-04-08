// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Binary, PlatformDependentQualifier, Templates} from "Sdk.Native.Shared";

import * as Link from "Sdk.Native.Tools.Link";

export declare const qualifier : PlatformDependentQualifier;

/**
 * The default extension of the output file.
 */
const outputFileExtension : PathAtom = PathAtom.create(".exe");

/**
 * Arguments for the NativeBinary transformer
 */
@@public
export interface Arguments extends Binary.NativeBinaryArguments {
    /** Delay loaded dynamic link libraries passed to the linker */
    delayLoadDlls?: String[];

    /** Enables the Windows Software Trace Preprocessor, commonly known as the WPP preprocessor */
    runWpp?: boolean;
}

/** Default exe arguments */
@@public
export const defaultArgs = Binary.defaultNativeBinaryArguments.override<Arguments>({
    delayLoadDlls: [],
    runWpp: false
});

/**
 * Represents a native executable
 */
@@public
export interface NativeExeImage extends Binary.NativeBuiltImage {}

/** Build an exe */
@@public
export function build(args: Arguments): NativeExeImage {
    args = defaultArgs
        .merge<Arguments>(Templates.defaultNativeExeBuilderTemplate)
        .merge<Arguments>(args);

    let includePaths: (File | StaticDirectory)[] = Binary.computeAllIncludeSearchPaths(args);
    let libraries: (File | StaticDirectory)[]    = (args.libraries || []);
    let clSources: File[]                        = [];
    let resources: (File | StaticDirectory)[]    = [];
    let rcSources: File[]                        = [];

    // Support the case when outputFileName has no extension
    if (!args.outputFileName.hasExtension ||
        !args.outputFileName.extension.equals(outputFileExtension, true)) {
        args = args.override<Arguments>({
                outputFileName: args.outputFileName.concat(outputFileExtension)
            });
    }

    //
    // phase 0:
    //

    let mcOutputs = Binary.runMessageCompiler(args);
    includePaths = includePaths.concat(mcOutputs.values().map(item => item.header));
    resources = resources.concat(mcOutputs.values().map(item => item.languageBinaryResources));
    rcSources = rcSources.concat(mcOutputs.values().map(item => item.resourceCompilerScript));

    let etwOutputs = Binary.runEtwManifestCompiler(args);
    includePaths = includePaths.concat(etwOutputs.values().map(item => item.header));
    resources = resources.concat(etwOutputs.values().map(item => item.binaryResources));
    rcSources = rcSources.concat(etwOutputs.values().map(item => item.resourceCompilerScript));
    
    let rcOutputs = Binary.runResourceCompiler(args, rcSources, resources, includePaths);

    if (args.runWpp) {
        let wppOutputs = Binary.runWppPreprocessorCompiler(args);
        includePaths = includePaths.push(wppOutputs.traceOutput);
    }

    //
    // phase 1: compile and generate OBJ
    //
    let clOutputs = Binary.runCppCompiler(args, includePaths, clSources);

    //
    // phase 2: generate Exe
    //
    let linkArgs = <Link.Arguments>{
        projectType: Link.LinkProjectType.executable,
        delayLoadDlls: args.delayLoadDlls,
    };

    //TODO: pass importLibrary (from running Lib???)
    let linkOutputs = Binary.runLink(
        args, 
        clOutputs,
        rcOutputs.values().map(r => r.resFile),
        libraries,
        undefined,
        linkArgs
    );

    return <NativeExeImage>{
        binaryFile: linkOutputs.binaryFile,
        debugFile: linkOutputs.debugFile,
        rcRunnerOutputs: rcOutputs,
        mcHeaderRunnerOutputs: mcOutputs,
        etwManifestRunnerOutputs: etwOutputs,
    };
}
