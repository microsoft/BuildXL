// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Binary {
    @@public
    export const defaultNativeBinaryArguments: NativeBinaryArguments = {
        outputFileName: undefined,
        sources: [],
        preprocessorSymbols: [
            {name: "_WINDOWS"},
            {name: "_USRDLL"},
            {name: "_WINDLL"},
            {name: "_UNICODE"},
            {name: "UNICODE"},
            {name: "VERBOSE", value: "0"},
        ],
        includes: [],
        frameworks: [],
        libraries: [],
        resources: [],
        platform: Shared.Platform.x86,
    };

    @@public
    export interface NativeBinaryTemplates {
        clRunner?: Cl.ClOptions;
        linkRunner?: Link.Arguments;
        libRunner?: Lib.Arguments;
        etwManifestRunner?: EtwManifest.Arguments;
        mcRunner?: Mc.Arguments;
        rcRunner?: Rc.Arguments;
        wppRunner?: Wpp.WppOptions;
    }

    /**
     * Arguments for the NativeBinary transformer
     */
    @@public
    export interface NativeBinaryArguments {
        /** A list of standard frameworks that this binary depends on. */
        frameworks?: Shared.NativeFramework[];

        /** Both include paths and local include files not on the explicit  (e.g. headers relative to sources). */
        includes?: (File | StaticDirectory)[];

        /** Inner templates. */
        innerTemplates?: NativeBinaryTemplates;

        /** Additional libraries to link to */
        libraries?: (File | StaticDirectory)[];

        /**
         * Specifies value enabling link time code generation of objects compiled with /GL (Whole Program Optimization).
         * This value decides whether ProfileGuidedOptimizationData or OutputProfileGuidedInstrumentationName are used.
         */
        linkTimeCodeGeneration?: Link.LinkTimeCodeGenerationOption;

        /** File name of the target executable/library */
        outputFileName?: PathAtom;

        /** Path override for output database file for use with Profile Guided Optimization training */
        outputProfileGuidedInstrumentationName?: PathAtom;

        /** The target platform for the binary. */
        platform?: Shared.Platform;

        /** Specifies the name of the header (e.g.,"stdafx.h") */
        precompiledHeaderName?: PathAtom;

        /** Preprocessor definitions to pass to cl.exe */
        preprocessorSymbols?: Shared.PreprocessorSymbol[];

        /**
         * Specifies the databases that will be consumed by profile-guided optimization.
         * If multiple databases are specified, they must be located in the same directory, and must follow the naming convention of "[output binary name]![postfix].pgd"
         */
        profileGuidedOptimizationData?: File[];

        /** A list of all the resources used by the Rc Compiler */
        resources?: File[];

        /**
         * A response file that will be passed to Cl/Lib/Link runners.
         * Intended for use of a set of common defines.
         */
        responseFile?: File;

        /** Source code files for the NativeBinary */
        sources: Shared.SourceFileArtifact[];

        /**
         * Specify the target subsystem for the binary.
         * This takes precedence over any innerTemplates definition.
         */
        subsystem?: Shared.Subsystem;
    }

    /**
     * Interface to represent inner runners of a builder.
     */
    @@public
    export interface NativeBuiltImage extends Shared.BuiltImage, Shared.PlatformBinary {
        /** A map from a filename to the output of EtwManifest runner. */
        etwManifestRunnerOutputs?: Map<PathAtom, EtwManifest.Result>;

        /** A map from a filename to the output of Mc runner. */
        mcHeaderRunnerOutputs?: Map<PathAtom, Mc.McOutput>;
        
        /** A map from a filename to the output of Rc runner. */
        rcRunnerOutputs?: Map<PathAtom, Rc.RcOutput>;
    }

    /** The default BuildXL-specific Link arguments */
    const defaultLinkTemplate = <Link.Arguments> {
        programDatabaseAlternatePath: "%_PDB%",
        linkTimeCodeGeneration: Link.LinkTimeCodeGenerationOption.use,
        generateDebugInformation: true
    };

    /** The default BuildXL-specific Lib arguments */
    const defaultLibTemplate = <Lib.Arguments> {
        inputFormat: Lib.InputFormat.commonObjectFileFormatAndLTCG,
        treatWarningAsError: true
    };

    /** The default BuildXL-specific CL arguments */
    const defaultClTemplate = <Cl.Arguments>{
        // from default template
        enablePreFast: true,
        treatWarningAsError: true,
        warningLevel: Cl.ClWarningLevel.enableAllWarnings,
        bufferSecurityCheck: true,
        exceptionHandling: Cl.ExceptionHandling.async,
        omitDefaultLibraryName: false,
        debugInformationFormat: Cl.DebugInformationFormat.oldStyle,
        enableOptimizedDebugInformation: true,
        runtimeLibrary: qualifier.configuration === "debug"
            ? Cl.RuntimeLibrary.multithreadedDebugDll
            : Cl.RuntimeLibrary.multithreadedDll,
        optimizations: qualifier.configuration === "debug"
            ? Templates.clDebugOptimizations
            : Templates.clReleaseOptimizations,
        useFullPaths: true,
        treatWchartAsBuiltInType: false,
        forceConformanceInForLoopScope: true,
        disableSpecificWarnings: [ 4711, 4595 ],
        sources: undefined,
        preprocessorSymbols: [
            {name: "_STL_WARNING_LEVEL", value: "3" }
        ]
    };

    /** Computes all the include search paths from the NativeBinaryArguments */
    @@public
    export function computeAllIncludeSearchPaths(args: NativeBinaryArguments): (File | StaticDirectory)[] {
        return [
            ...(args.includes || []),
            ...(args.frameworks || []).mapMany(f => f.includeSearchPaths || [])
        ];
    }

    /** Compute all the preprocessor symbols from the NativeBinaryArguments and a list of additional symbols */
    @@public
    export function computeAllPreprocessorSymbols(args: NativeBinaryArguments, additionalSymbols?: Shared.PreprocessorSymbol[]): Shared.PreprocessorSymbol[] {
        return [
            ...args.preprocessorSymbols,
            ...args.frameworks.mapMany(f => f.preprocessorSymbols),
            ...(additionalSymbols || [])
        ];
    }

    /** Run the etw manifest compiler */
    @@public
    export function runEtwManifestCompiler(args: NativeBinaryArguments): Map<PathAtom, EtwManifest.Result> {
        let etwTemplate = (args.innerTemplates && args.innerTemplates.etwManifestRunner) || {};

        return EtwManifest.compile(etwTemplate.merge<EtwManifest.Arguments>({
            sources: args.sources.filter(EtwManifest.isEtwManFile),
        }));
    }

    /** Run the cpp compiler */
    @@public
    export function runCppCompiler(
            args: NativeBinaryArguments,
            includes: (File | StaticDirectory)[],
            generatedSources?: File[]): Cl.CompilationOutput[] {

        let clTemplate = defaultClTemplate.merge<Cl.Arguments>(args.innerTemplates && args.innerTemplates.clRunner);
        let clArgs: Cl.Arguments = clTemplate
            .merge<Cl.Arguments>({ // must use merge here
                sources: [
                    ...args.sources.filter(Cl.isClFile),
                    ...(generatedSources || [])
                ],
                includes: includes,
                precompiledHeaderName: args.precompiledHeaderName,
                injectPrecompiledHeaderReference: args.outputFileName.changeExtension("").toString(),
                preprocessorSymbols: computeAllPreprocessorSymbols(args)
            });

        if (clArgs.responseFile === undefined && args.responseFile !== undefined) {
            clArgs = clArgs.overrideKey<Cl.Arguments>("responseFile", args.responseFile);
        }

        let clResult = Cl.compile(clArgs);

        return [
            ...clResult.compilationOutputs.values(),
            ...(clResult.precompiledHeader ? [clResult.precompiledHeader] : [])
        ];
    }

    /** Run the linker */
    @@public
    export function runLink(
            args: NativeBinaryArguments,
            objectFiles: Cl.CompilationOutput[],
            resourceFiles: File[],
            libraries: (File | StaticDirectory)[],
            importLibrary: Lib.Result,
            specificLinkArgs?: Link.Arguments): Link.Result {

        let linkTemplate = defaultLinkTemplate.merge<Link.Arguments>(args.innerTemplates && args.innerTemplates.linkRunner);
        let linkArgs = linkTemplate
            .merge<Link.Arguments>({
                importLibrary: importLibrary,
                outputFile: args.outputFileName,
                sources: objectFiles,
                resources: resourceFiles,
                libraries: libraries,
                linkTimeCodeGeneration: args.linkTimeCodeGeneration || Link.LinkTimeCodeGenerationOption.use,
                outputProfileGuidedInstrumentationName: args.outputProfileGuidedInstrumentationName,
                profileGuidedOptimizationData: args.profileGuidedOptimizationData
            })
            .merge<Link.Arguments>(specificLinkArgs || {});

        if (linkArgs.responseFile === undefined && args.responseFile !== undefined) {
            linkArgs = linkArgs.overrideKey<Link.Arguments>("responseFile", args.responseFile);
        }

        if (args.subsystem !== undefined) {
            linkArgs = linkArgs.overrideKey("subsystem", args.subsystem);
        }

        return Link.evaluate(linkArgs);
    }

    /** Run lib.exe */
    @@public
    export function runLib(
            args: NativeBinaryArguments,
            objectFiles: Cl.CompilationOutput[],
            libraries?: (File | StaticDirectory)[]): Lib.Result {

        let libTemplate = defaultLibTemplate.merge<Lib.Arguments>(args.innerTemplates && args.innerTemplates.libRunner);
        let libArgs = libTemplate.merge<Lib.Arguments>({
            outputFileName: args.outputFileName.changeExtension(".lib"),
            platform: args.platform,
            inputFormat: (args.linkTimeCodeGeneration === undefined)
                ? Lib.InputFormat.commonObjectFileFormat
                : Lib.InputFormat.commonObjectFileFormatAndLTCG,
            sources: objectFiles,
            libraries: (libraries || []).mapDefined(lib => typeof lib === "File" ? lib as File : undefined),
        });

        if (libArgs.responseFile === undefined && args.responseFile !== undefined) {
            libArgs = libArgs.overrideKey<Lib.Arguments>("responseFile", args.responseFile);
        }

        return Lib.evaluate(libArgs);
    }

    /** Process a .def file */
    @@public
    export function processDefFiles(
            args: NativeBinaryArguments,
            runCPreprocessor: boolean,
            includes: (File | StaticDirectory)[]): File {

        let defFiles = args.sources.filter(CPreprocessor.isDefFile);

        if (defFiles === undefined || defFiles.length === 0) {
            return undefined;
        }

        Contract.requires(defFiles.length === 1, "cannot specify more than one DEF file");

        if (defFiles[0] !== undefined && runCPreprocessor) {
            return CPreprocessor.evaluate({
                source: defFiles[0],
                includes: includes,
                preprocessorSymbols: computeAllPreprocessorSymbols(args, [{name: "PREPROCESSDEF"}])
            });
        } else {
            return Shared.getFile(defFiles[0]);
        }
    }

    /** Run the message compiler with an MCHeader as an input (.man) */
    @@public
    export function runMessageCompiler(args: NativeBinaryArguments): Map<PathAtom, Mc.McOutput> {
        let mcFiles = args.sources.filter(Mc.isMcFile);
        let mcTemplate = (args.innerTemplates && args.innerTemplates.mcRunner) || {};

        if (mcFiles.isEmpty()) {
            return Map.empty<PathAtom, Mc.McOutput>();
        }

        let mcArgs = mcTemplate
        .merge<Mc.Arguments>({
            sources: mcFiles
        });

        return Mc.evaluate(mcArgs);
    }

    /** Run the resource compiler */
    @@public
    export function runResourceCompiler(
        args: NativeBinaryArguments,
        generatedSources?: File[],
        generatedResources?: (File | StaticDirectory)[],
        includes?: (File | StaticDirectory)[]): Map<PathAtom, Rc.RcOutput> {

        let sources = [
            ...(args.sources.filter(Rc.isRcFile)),
            ...(generatedSources || []),
        ];

        if (sources.isEmpty()) {
            return Map.empty<PathAtom, Rc.RcOutput>();
        }

        let rcTemplate = (args.innerTemplates && args.innerTemplates.rcRunner) || {};

        let rcArgs = rcTemplate.merge<Rc.Arguments>({
            sources: sources,
            includes: includes || [],
            preprocessorSymbols: args.preprocessorSymbols, // call computeAllPreprocessorSymbols???
            resources: [
                ...args.resources,
                ...generatedResources
            ]
        });

        return Rc.evaluate(rcArgs);
    }

    /** Run the Wpp preprocessor compiler */
    @@public
    export function runWppPreprocessorCompiler(args: NativeBinaryArguments): Wpp.Result {
        let sources = args.sources.filter(Cl.isClFile);

        let wppTemplate = (args.innerTemplates && args.innerTemplates.wppRunner) || {};

        let wppArgs = wppTemplate.override<Wpp.Arguments>({
            sources: sources
        });
        
        return Wpp.evaluate(wppArgs);
    }
}
