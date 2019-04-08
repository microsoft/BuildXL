// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Binary, PlatformDependentQualifier, Templates} from "Sdk.Native.Shared";

export declare const qualifier : PlatformDependentQualifier;

/** Static library arguments */
@@public
export interface Arguments extends Binary.NativeBinaryArguments {}

/** Default arguments */
@@public
export const defaultArgs = Binary.defaultNativeBinaryArguments;

/**
 * Represents a native static library
 */
@@public
export interface NativeStaticLibraryImage extends Binary.NativeBuiltImage {}

/** Build a static library */
@@public
export function build(args: Arguments): NativeStaticLibraryImage {
    args = defaultArgs
        .merge<Arguments>(Templates.defaultStaticLibraryBuilderTemplate)
        .merge<Arguments>(args);

    let includePaths: (File | StaticDirectory)[] = Binary.computeAllIncludeSearchPaths(args);
    let clSources: File[]                        = [];
    let rcSources: File[]                        = [];
    let resources: (File | StaticDirectory)[]    = [];

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

    //
    // phase 1: compile and generate OBJ
    //
    let clOutputs = Binary.runCppCompiler(args, includePaths, clSources);

    //
    // phase 2: generate Lib
    //
    let libOutputs = Binary.runLib(args, clOutputs);

    return <NativeStaticLibraryImage> {
        binaryFile: libOutputs.binaryFile,
        debugFile: undefined,
        etwManifestRunnerOutputs: etwOutputs,
        rcRunnerOutputs: rcOutputs,
        mcHeaderRunnerOutputs: mcOutputs,
    };
}
