// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Tool} from "Sdk.Transformers";

import {Binary, PlatformDependentQualifier, Templates} from "Sdk.Native.Shared";

import * as Link from "Sdk.Native.Tools.Link";

export declare const qualifier : PlatformDependentQualifier;

/**
 * The default extension of the output file.
 */
const outputFileExtension : PathAtom = PathAtom.create(".dll");

/**
 * Arguments for the NativeBinary transformer
 */
@@public
export interface Arguments extends Binary.NativeBinaryArguments {
    /** Should the preprocessor be run on the .def File. */
    preprocessModuleDefinitionFile?: boolean;

    /** Specifies the exports for the binary */
    exports?: Link.ExportSymbol[];

    /**
     * Stipulates that the builder should create the Import Library (IMPLIB) and Export File
     * with a separate invocation of lib.exe.
     */
    createExportLibrarySeparately?: boolean;

    /** Delay loaded dynamic link libraries passed to the linker */
    delayLoadDlls?: String[];

    /** Prevents the linker from registering an entry point for the DLL */
    resourceOnlyDll?: boolean;

    /** Enables the Windows Software Trace Preprocessor, commonly known as the WPP preprocessor */
    runWpp?: boolean;
}

/** Default dll arguments */
@@public
export const defaultArgs = Binary.defaultNativeBinaryArguments.override<Arguments>({
    preprocessModuleDefinitionFile: false,
    exports: [],
    createExportLibrarySeparately: false,
    resourceOnlyDll: false,
    runWpp: false
});

/**
 * Represents a native dll
 */
@@public
export interface NativeDllImage extends Binary.NativeBuiltImage {
    /** A link to the associated .lib file for this native Dll. */
    importLibrary: File;
}

/** Build a dll */
@@public
@@Tool.builder(
    <Tool.BuilderMetadata>{
        name: "NativeDll",
        invokesTransformers: [
            "cl.exe",
            "cl -p",
            "mc.exe (etw manifest)",
            "link -lib",
            "link.exe",
            "mc.exe (mc)",
            "rc.exe",
            "tracewpp.exe"
        ]
    })
export function build(args: Arguments): NativeDllImage {
    args = defaultArgs
        .merge<Arguments>(Templates.defaultNativeDllBuilderTemplate)
        .merge<Arguments>(args);

    let includePaths: (File | StaticDirectory)[] = Binary.computeAllIncludeSearchPaths(args);
    let libraries: (File | StaticDirectory)[]    = args.libraries;
    let resources: (File | StaticDirectory)[]    = [];
    let clSources: File[]                        = [];
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

    let defFile = Binary.processDefFiles(args, args.preprocessModuleDefinitionFile, includePaths);

    if (args.createExportLibrarySeparately && args.resourceOnlyDll) {
        Contract.fail("'ResourceOnlyDll' does not have exports");
    }

    let libOutputs = args.createExportLibrarySeparately
        ? Binary.runLib(args, clOutputs, libraries)
        : undefined;

    //
    // phase 2: generate Dll
    //
    let linkArgs = <Link.Arguments>{
        projectType: Link.LinkProjectType.dynamicLinkLibrary,
        resourceOnlyDll: args.resourceOnlyDll,
        delayLoadDlls: args.delayLoadDlls,
        importLibrary: undefined,
        moduleDefinitionFile: defFile,
        exports: args.exports,
    };

    let linkOutputs = Binary.runLink(
        args,
        clOutputs,
        rcOutputs.values().map(r => r.resFile),
        libraries,
        libOutputs,
        linkArgs
    );

    return <NativeDllImage> {
        binaryFile: linkOutputs.binaryFile,
        debugFile: linkOutputs.debugFile,
        importLibrary: linkOutputs.importLibrary,
        rcRunnerOutputs: rcOutputs,
        mcHeaderRunnerOutputs: mcOutputs,
    };
}
