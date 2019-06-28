// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

import {Shared, PlatformDependentQualifier} from "Sdk.Native.Shared";

export declare const qualifier: PlatformDependentQualifier;

/** Default cl runner arguments */
export const defaultClArguments: Arguments = {
    suppressStartupBanner: true,
    preprocessorSymbols: [],
    undefinePreprocessorSymbols: [],
    ignoreStandardIncludePath: true,
    useRegistryForImport: false,
    runtimeTypeInfo: false,
    runtimeChecks: RuntimeChecks.empty,
    managedCompilation: ManagedCompilation.disabled,
    treatWchartAsBuiltInType: true,
    debugInformationFormat: DebugInformationFormat.oldStyle,
    omitDefaultLibraryName: true,
    warningLevel: ClWarningLevel.level1,
    forceProfileGuidedOptimizationSpeculativeDevirtualization: 0,
    runtimeLibrarySupportForVectorOperations: true,
    defineDateAndTime: false,
    allowBleedingFunctions: true,
    emitEpilogUnwindCodes: false,
    preprocessorStandardOutputRouting: PreprocessorStandardOutputRouting.noStandardOutputRouting,
    warningsToLevelMap: Map.empty<ClWarningLevel, number[]>(),
    optimizations: {
        optimizationMode: OptimizationMode.disabled
    },
    injectPrecompiledHeaderReference: "Windows",
    sources: [],
    includes: [],
    additionalUsingDirectories: [],
    precompiledHeaderMemoryAllocationFactor: 100,

    precompiledHeaderSourceFile: undefined,
    precompiledHeaderName: undefined
};

/** Compile the arguments into a cl result */
@@Tool.runner("cl.exe")
@@public
export function compile(args: Arguments): Result {
    args = defaultClArguments.override<Arguments>(args);

    let cmdArgs: Argument[] = [];
    let implicitInputs: Transformer.InputArtifact[] = [];
    let pchOutput: CompilationOutput = undefined;
    if (args.precompiledHeaderName) {
        let pchResult = createPrecompiledHeader(args);
        pchOutput = pchResult[0];

        cmdArgs = [
            Cmd.option("/Yu", args.precompiledHeaderName.toString()),
            Cmd.option("/Fp", Artifact.input(pchResult[1])),
        ];

        if (pchResult[2] !== undefined) {
            implicitInputs = implicitInputs.push(pchResult[2]);
        }
    }

    // TODO: if this is too slow, consider introducing function 'aggregateMap' (as well as
    //        'aggregateArray' and 'aggregateSet') on arrays, which can create a single Dictionary
    //        and mutate it.
    let outs = args.sources.reduce(
        (acc, src, idx) => {
            let sourceOutDir = Context.getNewOutputDirectory("cl");
            let sourceCmdArgs = cmdArgs;

            if (typeof src !== "File") {
                let clFile = src as ClFile;
                let compilationType = clFile.compilationType;
                sourceCmdArgs = sourceCmdArgs.push(clFileToCmdLineArgs(sourceOutDir, compilationType));
            }

            return acc.add(
                Shared.getFile(src).path.name.toString(),
                evaluateOneSourceFile(sourceOutDir, args, Shared.getFile(src), sourceCmdArgs, implicitInputs, [])[0]);
        },
        Map.empty<string, CompilationOutput>()
    );

    return <Result>{
        compilationOutputs: outs,
        precompiledHeader: pchOutput
    };
}

/**
 * Determines if a SourceFileArtifact is a cl runner file
 */
@@public
export function isClFile(source: Shared.SourceFileArtifact): boolean {
    if (typeof source === "File") {
        let extension = (source as File).extension;
        return extension === a`.c` || extension === a`.cpp` || extension === a`.cxx`;
    }
    
    return isSourceFileClFile(source as Shared.SourceFileContainer);
}

/**
 * Creates a clFile
 */
@@public
export function clFile(f: File, compilationType?: CompilationType): ClFile {
    if (compilationType === undefined) compilationType = getCompilationTypeFromExtension(f);
    return <ClFile>{ source: f, kind: "cl", compilationType: compilationType };
}

//-----------------------------------------------------------------------------
//
//                              CLFILE LOGIC
//
//-----------------------------------------------------------------------------

function isSourceFileClFile(source: Shared.SourceFileContainer): source is ClFile {
    return ((source as ClFile).kind === "cl");
}

//-----------------------------------------------------------------------------
//
//                       COMPILATION TYPE LOGIC
//
//-----------------------------------------------------------------------------

function getCompilationTypeFromExtension(f: File): CompilationType {
    let extension = f.extension;
    if (extension === a`.c`) return CompilationType.compileAsC;
    if (extension !== a`.cpp` && extension !== a`.cxx`) Contract.fail("SourceFile has an unsupported extension: " + f.toString());
    return CompilationType.compileAsCpp;
}

function getLanguageSelectorOption(compilationType: CompilationType): string {
    if (compilationType === undefined) return undefined;
    if (compilationType === CompilationType.compileAsCpp) return "/TP";
    Contract.assert(compilationType === CompilationType.compileAsC);
    return "/TC";
}

function verifyPCHSourcesCompilationTypes(sources: Shared.SourceFileArtifact[]): CompilationType {
    let sourcesCompilationType: CompilationType = undefined;
    for (let source of sources) {
        let sourceCompilationType = (typeof source === "File" ? getCompilationTypeFromExtension(Shared.getFile(source)) : (<ClFile>source).compilationType);
        if (sourcesCompilationType !== undefined && sourcesCompilationType !== sourceCompilationType) {
            Contract.fail("the file type of :" + Shared.getFile(source) + " is:" + sourceCompilationType + " which differs from the type of the other files:" + 
                                sourcesCompilationType + ". This is not allowed if a precompiledHeader is used");
        }

        sourcesCompilationType = sourceCompilationType;
    }

    return sourcesCompilationType;
}

function createPrecompiledHeader(args: Arguments): [CompilationOutput, DerivedFile, DerivedFile] {
    Contract.requires(args.debugInformationFormat === DebugInformationFormat.oldStyle, "Precompiled headers require 'oldStyle' debug");
    Contract.requires(args.precompiledHeaderName !== undefined, "precompiledHeaderName not specified");

    let outDir = Context.getNewOutputDirectory("cl-stdafx.pch");
    let precompiledHeaderCompilationType = verifyPCHSourcesCompilationTypes(args.sources);

    let srcFile = args.precompiledHeaderSourceFile;
    if (srcFile === undefined) { 
        let srcFilePath = outDir.combine(args.precompiledHeaderName).changeExtension(".src");
        srcFile = Transformer.writeAllText({
            outputPath: srcFilePath, 
            text: "#include <" + args.precompiledHeaderName + ">"
        });
    }
    let pchOutFile = outDir.combine(args.precompiledHeaderName).changeExtension(".pch");

    // The precompiled header (referenced by PrecompiledHeaderName) will be read during compilation of the .PCH file but
    // it will not be read during usage of the .PCH during compilation of the rest of the source files (below) as the compiler
    // will just insert the already compiled .PCH whenever it encounters a #include that references the precompiled header name.
    let cmdArgs = [
        Cmd.option("/Yc", args.precompiledHeaderName.toString()),
        Cmd.option("/Fp", Artifact.output(pchOutFile)),
        Cmd.argument(getLanguageSelectorOption(precompiledHeaderCompilationType)),
        Cmd.option("/Zm", args.precompiledHeaderMemoryAllocationFactor !== 100 ? args.precompiledHeaderMemoryAllocationFactor : undefined),
        ...(args.debugInformationFormat === DebugInformationFormat.oldStyle
            ? [ Cmd.option("/Yl", args.injectPrecompiledHeaderReference) ]
            : []
        )
    ];

    let pchOutAst: Path = undefined;
    if (args.enablePreFast) {
        pchOutAst = pchOutFile.changeExtension(".pchast");
    }

    let ans = evaluateOneSourceFile(
                    outDir, 
                    args,
                    srcFile, 
                    cmdArgs, 
                    [f`${Context.getSpecFileDirectory().combine(args.precompiledHeaderName)}`],
                    (pchOutAst !== undefined) ? [pchOutAst] : []);

    let pch = ans[1].getOutputFile(pchOutFile);

    let pchAst: DerivedFile = undefined;
    if (pchOutAst !== undefined) {
        pchAst = ans[1].getOutputFile(pchOutAst);
    }

    return [ans[0], pch, pchAst];
}

//-----------------------------------------------------------------------------
//
//                              ARGUMENTS LOGIC
//
//-----------------------------------------------------------------------------

function evaluateOneSourceFile(
    outDir: Directory, 
    args: Arguments, 
    source: File, 
    extraCmdArgs: Argument[], 
    implicitInputs: Transformer.InputArtifact[], 
    implicitOutputs: Transformer.OutputArtifact[]): 
    [CompilationOutput, Transformer.ExecuteResult] {
    let objOutFile = outDir.combine(source.path.name).changeExtension(".obj");
    let pdbOutFile = (args.debugInformationFormat !== DebugInformationFormat.none &&
                        args.debugInformationFormat !== DebugInformationFormat.oldStyle)
                    ? objOutFile.changeExtension(".pdb")
                    : undefined;

    let includes          = args.includes.mapDefined(include => typeof include === "File" ? include as File : undefined);
    let includeSearchDirs = args.includes.mapDefined(include => typeof include !== "File" ? include as StaticDirectory : undefined);

    let analysisOutFile = args.enablePreFast === true ? objOutFile.changeExtension(".nativecodeanalysis.xml") : undefined;

    let cmdArgs: Argument[] = [
        ...optionsToCmdLineArgs(args, !includes.isEmpty(), includeSearchDirs),

        Cmd.option("/Fo", Artifact.output(objOutFile)),
        Cmd.option("/Fd", Artifact.output(pdbOutFile)),
        ...(analysisOutFile ? [
            Cmd.argument("/analyze"),
            Cmd.argument("/analyze:quiet"),
            Cmd.option("/analyze:log", Artifact.output(analysisOutFile))
        ] : []),

        ...extraCmdArgs,

        Cmd.argument(Artifact.input(source))
    ];

    let result = Transformer.execute({
        tool: args.tool || importFrom("VisualCpp").clTool,
        workingDirectory: outDir,
        arguments: cmdArgs,
        dependencies: [...includes, ...implicitInputs],
        implicitOutputs: implicitOutputs,
        tags: args.tags,
    });

    let compOutput = <CompilationOutput> {
        binary: {
            object: result.getOutputFile(objOutFile),
            pdb: pdbOutFile && result.getOutputFile(pdbOutFile)
        },
        analysisResult: analysisOutFile && result.getOutputFile(analysisOutFile),
    };

    return [compOutput, result];
}

function optionsToCmdLineArgs(opts: ClOptions, includeLocalDir: boolean, includeSearchDirs: StaticDirectory[]): Argument[] {
    /**
     * NOTE: When an assembly listing file is specified along with wholeProgram,
     * the listing doesn't get generated by cl.exe, but it does eventually get generated by link.exe if that .obj is needed by the final binary.
     * This behavior is currently not supported in BuildXL.
     */
    Contract.requires(opts.assemblerOutput === undefined || !opts.optimizations.wholeProgram,
                        "cannot have wholeProgram optimization on and generate an assembly file");
    Contract.assert(opts.removePrefixFromFileStrings === undefined, "Not implemented because we don't have support for option suffixes in Cmd");
    Contract.assert(!(opts.kernelModeBinary === true && opts.exceptionHandling !== undefined), "CPP exceptions are not valid in kernel mode");
    Contract.assert(!(opts.callingConvention !== undefined &&
                            (opts.managedCompilation === ManagedCompilation.pure ||
                                opts.managedCompilation === ManagedCompilation.safe)),
                            "Unsupported calling convention given managed compilation option");

    // seal include dirs
    return [
        Cmd.flag("/nologo", opts.suppressStartupBanner),
        Cmd.option("@", Artifact.input(opts.responseFile)),
        Cmd.startUsingResponseFile(),
        Cmd.argument("/c"), // compile without linking; Lib and Link should be separate discrete pips

        // include options
        Cmd.flag("/X",                    opts.ignoreStandardIncludePath),
        Cmd.options("/FI",                Artifact.inputs(opts.forcedIncludeFiles)),
        Cmd.option("/I",                  Artifact.none(Context.getSpecFileDirectory()), includeLocalDir),
        Cmd.options("/I",                 Artifact.inputs(includeSearchDirs)),
        Cmd.options("/AI",                Artifact.inputs(opts.additionalUsingDirectories)),
        Cmd.options("/FU",                Artifact.inputs(opts.forcedUsingFiles)),
        Cmd.flag("/d1import_no_registry", opts.useRegistryForImport === false),

        // warning options
        Cmd.option("/errorReport:", opts.errorReporting ? opts.errorReporting.toString() : undefined),
        Cmd.option("/Wv:",          opts.disableWarningsAfterVersion),
        Cmd.flag("/WX",             opts.treatWarningAsError),
        Cmd.options("/we",          opts.treatSpecificWarningsAsErrors),
        Cmd.options("/wo",          opts.singleIssueWarnings),
        Cmd.option("/W",            warningLevelToCmdLineFlagSuffix(opts.warningLevel)),
        // if all warnings are disabled, the following wouldn't have any effect
        ...(opts.warningLevel === ClWarningLevel.turnOffAllWarnings 
            ? []
            : [
                ...opts.warningsToLevelMap.forEach(kvp => Cmd.options("/w" + warningLevelToCmdLineFlagSuffix(kvp[0], true), kvp[1])),
                Cmd.options("/wd", opts.disableSpecificWarnings)
            ]),
        
        // optimization options
        Cmd.option("/O",  optimizationModeToCmdLineStringFlagSuffix(opts.optimizations.optimizationMode)),
        Cmd.option("/Ob", inlineFuncToCmdLineStringFlagSuffix(opts.optimizations.inlineFunctionExpansion)),
        Cmd.sign("/Oi", opts.optimizations.intrinsicFunctions, true),
        Cmd.sign("/Oy", opts.optimizations.optimizeFramePointer, true),
        Cmd.sign("/Gy", opts.optimizations.functionLevelLinking, true),
        Cmd.sign("/Gw", opts.optimizations.optimizeGlobalData, true),
        Cmd.sign("/GF", opts.optimizations.stringPooling, true),
        Cmd.sign("/GL", opts.optimizations.wholeProgram, true),
        Cmd.sign("/GA", opts.optimizations.optimizeThreadLocalStorageAccessForExe, true),
        Cmd.sign("/GT", opts.optimizations.fiberSafeThreadLocalStorage, true),
        Cmd.option("/favor:", Shared.mapEnumConst(opts.optimizations.favorProcessor,
            [FavorProcessor.blend, "blend"],
            [FavorProcessor.atom, "ATOM"],
            [FavorProcessor.amd64, "AMD64"],
            [FavorProcessor.intel64, "INTEL64"])),

        Cmd.sign("/GS",   opts.bufferSecurityCheck, true),

        // code generation options
        Cmd.sign("/Gm", opts.minimalRebuild, true),
        Cmd.sign("/GR", opts.runtimeTypeInfo, true),
        Cmd.option("/EH", exceptionHandlingToCmdLineFlagSuffix(opts.exceptionHandling)),
        Cmd.option("/fp:", Shared.enumConstantToString(opts.floatingPointModel)),
        Cmd.flag("/fp:except", opts.floatingPointExceptions),
        Cmd.option("/G", callingConventionToCmdLineFlagSuffix(opts.callingConvention)),
        Cmd.option("/arch:", eisToCmdLineFlagSuffix(opts.enhancedInstructionSet)),
        Cmd.option("/Gs", returnIfDefinedAndNotZeroElseReturnUndefined(opts.stackCheckingThreshold)),
        Cmd.option("/volatile:", Shared.enumConstantToString(opts.volatile)),
        Cmd.option("/RTC", Cmd.join("", runtimeCheckToCmdLineFlagSuffix(opts.runtimeChecks))),
        Cmd.option("/clr", managedCompilationToCmdLineFlagSuffix(opts.managedCompilation)),
        Cmd.flag("/Gy", opts.functionLevelLinking),
        Cmd.flag("/Fx", opts.expandAttributedSource),

        // preprocessor
        Cmd.options("/D", (opts.preprocessorSymbols || []).mapDefined(Shared.preprocessorSymbolToString)),
        Cmd.flag("/u", opts.undefineAllPreprocessorSymbols),
        Cmd.options("/U", opts.undefinePreprocessorSymbols),
        Cmd.flag("/C", opts.preprocessKeepComments),
        Cmd.flag("/showIncludes", opts.showIncludes),

        // language options
        Cmd.option("/Z",                  debugInfoFormatToCmdLineFlagSuffix(opts.debugInformationFormat)),
        Cmd.option("/Zp",                 structMemberAlignmentToCmdLineFlagSuffix(opts.structMemberAlignment)),
        Cmd.flag("/Za",                   opts.disableLanguageExtensions),
        Cmd.flag("/Zl",                   opts.omitDefaultLibraryName),

        Cmd.option("/std:",               languageVersionToCmdLineFlagSuffix(opts.languageVersion)),
        Cmd.sign("/permissive",           (opts.strictStandardsConformance !== undefined) ? !opts.strictStandardsConformance : undefined, true),
        Cmd.sign("/Zc:inline",            opts.removeUnreferencedCodeData, true),
        Cmd.sign("/Zc:implicitNoexcept",  opts.implicitNoExcept, true),
        Cmd.sign("/Zc:threadSafeInit",    opts.threadSafeLocalStaticInitialization, true),
        Cmd.sign("/Zc:sizedDealloc",      opts.permitSizedDeallocation, true),
        Cmd.sign("/Zc:wchar_t",           opts.treatWchartAsBuiltInType, true),
        Cmd.sign("/Zc:forScope",          opts.forceConformanceInForLoopScope, true),
        Cmd.sign("/Zc:rvalueCast",        opts.enforceStandardConversionRules, true),
        Cmd.sign("/Zc:auto",              opts.enforceStandardAutoKeyword, true),
        Cmd.sign("/Zc:trigraphs",         opts.allowTrigraphs, true),
        Cmd.sign("/Zc:strictStrings",     opts.strictStrings, true),
        Cmd.sign("/Zc:throwingNew",       opts.throwingNew, true),
        Cmd.sign("/Zc:referenceBinding",  opts.enforceStandardReferenceBinding, true),

        Cmd.flag("/ZW:nostdlib",          opts.winRTNoStdLib),
        Cmd.flag("/ZW",                   opts.compileAsWinRT),
        Cmd.flag("/openmp",               opts.openMPSupport),

        // runtime library options
        Cmd.option("/M", runtimeLibraryToCmdLineFlagSuffix(opts.runtimeLibrary)),

        // internal front-end options
        Cmd.flag("/d1versionLKG171",            opts.fakeVersionNumberToLkg171),
        Cmd.flag("/d1warningLKG171",            opts.suppressPostLkg171Warnings),
        Cmd.flag("/d1ignorePragmaWarningError", opts.ignorePragmaWarningError),
        /* Cmd.options("/d1trimfile:", Artifact.inputs(opts.removePrefixFromFileStrings), "\\"), // TODO: we don't have support for suffixes */
        Cmd.option("/d1clr:",                   coreLibraryVersionToCmdLineFlag(opts.setCoreLibraryVersion)),
        Cmd.flag("/d1nodatetime",               opts.defineDateAndTime === false),
        Cmd.flag("/d1vc7dname",                 opts.supportLegacyDecoratedNames),
        Cmd.flag("/d1NTcompat",                 opts.handleDownlevelCompatibility),
        Cmd.flag("/d1NonStandardNewDelete",     opts.allowNonStandardNewDelete),
        Cmd.flag("/d1uselinevar",               opts.useLineVariable),
        Cmd.flag("/d1WarnOnGlobals",            opts.warnOnGlobals),

        // internal back-end options
        Cmd.flag("/d2guard1",                    opts.enableInlineGuardChecks),
        Cmd.flag("/d2guard2",                    opts.enableInternalFunctionCallGuard),
        Cmd.flag("/d2guard4",                    opts.enableInternalControlFlowGuard),
        Cmd.flag("/d2guardss2",                  opts.enableReturnFlowGuard),
        Cmd.flag("/d2guardgsforcfg",             opts.enableExtraGSProtectionForControlFlowGuard),
        Cmd.flag("/d2nocfgrngchk",               opts.disableVirtualTableChecksForControlFlowGuard),
        Cmd.flag("/d2guardcfgdispatch",          opts.enableDispatchModeForControlFlowGuard),
        Cmd.flag("/d2guardlongjmp",              opts.enableGuardForLongJump),
        Cmd.option("/d2psvcallopt",              returnIfDefinedAndNotZeroElseReturnUndefined(opts.forceProfileGuidedOptimizationSpeculativeDevirtualization)),
        Cmd.flag("/d2Qvec-mathlib-",             opts.runtimeLibrarySupportForVectorOperations === false),
        Cmd.flag("/d2Qvec-",                     opts.forceVectorOperationsDisabled),
        Cmd.option("/d2guardcheckesp",           returnAddressValidationToCmdLineFlagSuffix(opts.generateReturnAddressValidation)),
        Cmd.flag("/d2Qvec-sse2only",             opts.assumeStreamingSingleInstructionMultipleDataExtensionsSupportOnly),
        Cmd.flag("/d2Zi+",                       opts.enableOptimizedDebugInformation),
        Cmd.flag("/d2AllowCompatibleILVersions", opts.allowCompatibleILVersions),
        Cmd.flag("/d2noftol3",                   opts.allowBleedingFunctions === false),
        Cmd.flag("/d2guardcfgfuncptr",           opts.callGuardCheckFunctionThruControlFlowGuardFunctionPointer),
        Cmd.flag("/d2epilogunwind",              opts.emitEpilogUnwindCodes),

        // other
        Cmd.flag("/homeparams", opts.copyRegisterParametersToStack),
        Cmd.flag("/FC",         opts.useFullPaths),
        Cmd.flag("/Gh",         opts.callPEnter),
        Cmd.flag("/GH",         opts.callPExit),
        Cmd.flag("/FR",         opts.browseInformation),
        Cmd.option("/FA",       assemblerOutputToCmdLineFlagSuffix(opts.assemblerOutput)),
        Cmd.flag("/FAu",        opts.useUnicodeForAssemblerListing),
        Cmd.flag("/doc",        opts.generateXmlDocumentation),
        Cmd.flag("/sdl",        opts.sdlCheck),
        Cmd.flag("/WL",         opts.enableOneLineDiagnostics),
        Cmd.flag("/await",      opts.enableCoroutines),
        Cmd.flag("/bigobj",     opts.generateExtendedObjectFormat),
        Cmd.flag("/hotpatch",   opts.createHotPatchableImage),
        Cmd.flag("/kernel",     opts.kernelModeBinary),
        Cmd.flag("/cbstring",   opts.forceCodeBaseStrings),
        Cmd.option("/E",        preprocStdOutToCmdLineFlagSuffix(opts.preprocessorStandardOutputRouting)),
        Cmd.flag("/P",          opts.preprocessorOutputToAFile),
        Cmd.option("/Fi",       Artifact.none(opts.preprocessorOutputDirectory)),
        Cmd.sign("/guard:cf",   opts.guardControlFlow, true),
        Cmd.flag("/brepro",     opts.compilerDeterminism),
        Cmd.flag("/ZH:SHA_256", opts.useSha256ForChecksum),
    ];
}

function clFileToCmdLineArgs(outDir: Directory, compilationType: CompilationType): Argument {
    return Cmd.argument(getLanguageSelectorOption(compilationType));
}

function structMemberAlignmentToCmdLineFlagSuffix(v: StructMemberAlignment): string {
    if (v === undefined) return undefined;
    switch (v) {
        case StructMemberAlignment.align1: return "1";
        case StructMemberAlignment.align2: return "2";
        case StructMemberAlignment.align4: return "4";
        case StructMemberAlignment.align8: return "8";
        case StructMemberAlignment.align16: return "16";
    }
}

function debugInfoFormatToCmdLineFlagSuffix(v: DebugInformationFormat): string {
    if (v === undefined) return undefined;
    switch (v) {
        case DebugInformationFormat.none: return undefined;
        case DebugInformationFormat.oldStyle: return "7";
        case DebugInformationFormat.programDatabase: return "i";
        case DebugInformationFormat.editAndContinue: return "I";
    }
}

function managedCompilationToCmdLineFlagSuffix(v: ManagedCompilation): string {
    if (v === undefined) return undefined;
    switch (v) {
        case ManagedCompilation.disabled: return undefined;
        case ManagedCompilation.mixed: return "";
        case ManagedCompilation.pure: return "pure";
        case ManagedCompilation.safe: return "safe";
    }
}

function runtimeCheckToCmdLineFlagSuffix(v: RuntimeChecks): string[] {
    return Shared.mapEnumFlags(v, 
            [ RuntimeChecks.reportDataLossOnTruncation, "c" ],
            [ RuntimeChecks.stackFrameRuntime, "s" ],
            [ RuntimeChecks.uninitializedLocalUsage, "u" ]
    );
}

function eisToCmdLineFlagSuffix(v: EnhancedInstructionSet): string {
    if (v === undefined) return undefined;
    switch (v) {
        case EnhancedInstructionSet.noExtensions: return "IA32";
        case EnhancedInstructionSet.streamingSimdExtensions: return "SSE";
        case EnhancedInstructionSet.streamingSimdExtensions2: return "SSE2";
        case EnhancedInstructionSet.advancedVectorExtensions: return "AVX";
        case EnhancedInstructionSet.advancedVectorExtensions2: return "AVX2";
        case EnhancedInstructionSet.armVectorExtension7VE: return "ARMv7VE";
        case EnhancedInstructionSet.armExtensionVFPv4: return "VFPv4";
        case EnhancedInstructionSet.armVectorFloatingPointExtensionV3D16: return "VFPv3-D16";
    }
}

function languageVersionToCmdLineFlagSuffix(v: LanguageVersion): string {
    if (v === undefined) return undefined;
    switch (v) {
        case LanguageVersion.cpp14: return "c++14";
        case LanguageVersion.cpp17: return "c++17";
        case LanguageVersion.cppLatest: return "c++latest";
    }
}

function callingConventionToCmdLineFlagSuffix(v: CallingConvention): string {
    if (v === undefined) return undefined;
    switch (v) {
        case CallingConvention.cdecl: return "d";
        case CallingConvention.fastCall: return "r";
        case CallingConvention.stdCall: return "z";
        case CallingConvention.vectorCall: return "v";
    }
}

function exceptionHandlingToCmdLineFlagSuffix(v: ExceptionHandling): string {
    if (v === undefined) return undefined;
    switch (v) {
        case ExceptionHandling.async: return "a";
        case ExceptionHandling.sync: return "sc";
        case ExceptionHandling.syncCThrow: return "s";
    }
}

function preprocStdOutToCmdLineFlagSuffix(v: PreprocessorStandardOutputRouting): string {
    if (v === undefined) return undefined;
    switch (v) {
        case PreprocessorStandardOutputRouting.noStandardOutputRouting: return undefined;
        case PreprocessorStandardOutputRouting.standardOutputWithLineDirectives: return "";
        case PreprocessorStandardOutputRouting.standardOutputWithNoLineDirectives: return "P";
    }
}

function assemblerOutputToCmdLineFlagSuffix(v: AssemblerOutput): string {
    if (v === undefined) return undefined;
    switch (v) {
        case AssemblerOutput.assemblyCode: return "";
        case AssemblerOutput.assemblyAndMachineCode: return "c";
        case AssemblerOutput.assemblyAndSourceCode: return "s";
        case AssemblerOutput.all: return "cs";
    }
}

function returnAddressValidationToCmdLineFlagSuffix(v: ReturnAddressValidation): string {
    if (v === undefined) return undefined;
    switch (v) {
        case ReturnAddressValidation.noValidation: return undefined;
        case ReturnAddressValidation.validateReturnAddressAndStackPointer: return "1";
        case ReturnAddressValidation.validateReturnAddress: return "2";
    }
}

function returnIfDefinedAndNotZeroElseReturnUndefined(n: number): number {
    if (n === undefined || n === 0) return undefined;
    return n;
}

function coreLibraryVersionToCmdLineFlag(v: CoreLibraryVersion): string {
    if (v === undefined) return undefined;
    switch (v) {
        case CoreLibraryVersion.useCustomLibrary: return "nomscorlib";
        case CoreLibraryVersion.skipCoreSystemDirectory: return "nostdlib";
        case CoreLibraryVersion.turnoffPureMsilRuntimeLibrary: return "nopurecrt";
        case CoreLibraryVersion.useOldCustomModifiers: return "oldCustomModifiers";
    }
}

function runtimeLibraryToCmdLineFlagSuffix(v: RuntimeLibrary): string {
    if (v === undefined) return undefined;
    switch (v) {
        case RuntimeLibrary.multithreaded: return "T";
        case RuntimeLibrary.multithreadedDebug: return "Td";
        case RuntimeLibrary.multithreadedDll: return "D";
        case RuntimeLibrary.multithreadedDebugDll: return "Dd";
    }
}

function inlineFuncToCmdLineStringFlagSuffix(v: InlineFunctionExpansion): string {
    if (v === undefined) return undefined;
    switch (v) {
        case InlineFunctionExpansion.disabled:           return "0";
        case InlineFunctionExpansion.onlyExplicitInline: return "1";
        case InlineFunctionExpansion.anySuitable:        return "2";
    }
}

function optimizationModeToCmdLineStringFlagSuffix(v: OptimizationMode): string {
    if (v === undefined) return undefined;
    switch (v) {
        case OptimizationMode.disabled:      return "d";
        case OptimizationMode.minimizeSpace: return "1";
        case OptimizationMode.maximizeSpeed: return "2";
        case OptimizationMode.maximumSafe:   return "x";
    }
}

function warningLevelToCmdLineFlagSuffix(v: ClWarningLevel, mustBe1Thru4?: boolean): string {
    Contract.requires(!mustBe1Thru4 ||
                    v === ClWarningLevel.level1 ||
                    v === ClWarningLevel.level2 ||
                    v === ClWarningLevel.level3 ||
                    v === ClWarningLevel.level4,
                    "Expected warning level from 1 to 4, got " + v);

    if (v === undefined) return undefined;
    switch (v) {
        case ClWarningLevel.turnOffAllWarnings: return "0";
        case ClWarningLevel.level1:             return "1";
        case ClWarningLevel.level2:             return "2";
        case ClWarningLevel.level3:             return "3";
        case ClWarningLevel.level4:             return "4";
        case ClWarningLevel.enableAllWarnings:  return "all";
    }
}

/**
    * Arguments for CL transformer
    */
@@public
export interface Arguments extends ClOptions, Transformer.RunnerArguments {
    /** InjectPrecompiledHeaderReference injects a single arbitrary symbol into an object module. */
    @@Tool.option("/Yl")
    injectPrecompiledHeaderReference?: string;

    /**
        * Specifies the precompiled header memory allocation limit.
        * 100 (the default) means 100% of default allocation limit. 200 would double default allocation limit.
        */
    @@Tool.option("/Zm")
    precompiledHeaderMemoryAllocationFactor?: number;

    /** Specifies the name of the header (e.g.,"stdafx.h") */
    @@Tool.option("/Y{c|u}")
    precompiledHeaderName?: PathAtom;

    /**
        * In the MSBuild precompiled header scheme, the precompiled header source file is
        * prepared by the engineer or perhaps by the Visual Studio wizard.  In either of
        * these scenarios, CLRunner does not have to generate it.
        */
    precompiledHeaderSourceFile?: File;

    /** Specifies a list of CL source files. */
    sources: Shared.SourceFileArtifact[];
}

/**
    * An input file with optional overrides.
    */
@@public
export interface ClFile extends Shared.SourceFileContainer {
    /** The file kind */
    kind: "cl";
    
    /** Specifies whether the file is to be compiled as a C file or as a C++ file */
    @@Tool.option("/T{C|P}")
    compilationType?: CompilationType;
}

/**
    * Options for CL transformer
    */
@@public
export interface ClOptions {
    /** List of directories that the compiler will search to resolve file references passed to the #using directive. */
    @@Tool.option("/AI")
    additionalUsingDirectories?: StaticDirectory[];

    /**
        * Allow ftol3 and other "bleeding" functions in the CRT
        * This corresponds to the opposite of CL's /d2noftol3 command-line argument.
        * Warning: this is an internal-use, undocumented option.  Use at your own risk.
        */
    @@Tool.option("/d2noftol3", { negateOption: true })
    allowBleedingFunctions?: boolean;

    /**
        * Allow compatible IL versions and don't require an exact match.
        * Warning: this is an internal-use, undocumented option.  Use at your own risk.
        */
    @@Tool.option("/d2AllowCompatibleILVersions")
    allowCompatibleILVersions?: boolean;

    /** Allow trigraph sequences. */
    @@Tool.option("/Zc:trigraphs")
    allowTrigraphs?: boolean;

    /**
        * Specifies the contents of assembly language output files.
        * This generates machine assembly output not to be confused with Managed Assembly.
        */
    assemblerOutput?: AssemblerOutput;

    /**
        * Only assume processor support for SSE and SSE2 vector operations
        * Warning: this is an internal-use, undocumented option.  Use at your own risk.
        */
    @@Tool.option("/d2Qvec-sse2only")
    assumeStreamingSingleInstructionMultipleDataExtensionsSupportOnly?: boolean;

    /** Enables browser information for IntelliSense functionality. */
    @@Tool.option("/FR")
    browseInformation?: boolean;

    /**
        * Enables or Disables dynamic detection of stack-buffer over-runs.
        * This security check helps detect stack-buffer over-runs, a common attempted attack upon a program's security.
        */
    @@Tool.option("/GS")
    bufferSecurityCheck?: boolean;

    /**
        * Call guard check function through CFG function pointer instead of calling the guard check function.
        * Warning: this is an internal-use, undocumented option.  Use at your own risk.
        */
    @@Tool.option("/d2guardcfgfuncptr")
    callGuardCheckFunctionThruControlFlowGuardFunctionPointer?: boolean;

    /** Controls the register calling conventions used for function calls. */
    callingConvention?: CallingConvention;

    /** Calls the _penter function at the entry of every method or function. */
    @@Tool.option("/Gh")
    callPEnter?: boolean;

    /** Calls the _pexit function at the end of every method or function. */
    @@Tool.option("/GH")
    callPExit?: boolean;

    /** Consume the Windows Runtime languages extensions. */
    @@Tool.option("/ZW")
    compileAsWinRT?: boolean;

    /** Copy parameters to the stack (in order to ease debugging of release builds on x64). */
    @@Tool.option("/homeparams")
    copyRegisterParametersToStack?: boolean;

    /** When hotpatching is on, the compiler ensures that first instruction of each function is two bytes, which is required for hot patching. */
    @@Tool.option("/hotpatch")
    createHotPatchableImage?: boolean;

    /**
        * Specifies the type of debugging information generated by the C/C++ compiler.
        * Linker settings must also be changed accordingly.
        */
    debugInformationFormat?: DebugInformationFormat;

    /**
        * Define __DATE__, __TIME__, and __TIMESTAMP__
        * This corresponds to the opposite of CL's /d1nodatetime command-line argument.
        * Warning: this is an internal-use, undocumented option.  Use at your own risk.
        */
    @@Tool.option("/d1nodatetime", { negateOption: true })
    defineDateAndTime?: boolean;

    /** Uses a backdoor to support linking against old libs with old decorated names for function template specializations. */
    @@Tool.option("/d1vc7dname")
    supportLegacyDecoratedNames?: boolean;

    /** Helps work around issues that are created when Dev14 is mixed with Dev12-FKG compiler. */
    @@Tool.option("/d1NTcompat")
    handleDownlevelCompatibility?: boolean;

    /** Allows new/delete to be declared inside of a namespace or be static. */
    @@Tool.option("/d1NonStandardNewDelete")
    allowNonStandardNewDelete?: boolean;

    /** If true, tells the compiler to emit an error for language constructs that are not compatible with either ANSI C or ANSI C++. */
    @@Tool.option("/Za")
    disableLanguageExtensions?: boolean;

    /** Disables the warning numbers that are specified in a semicolon-delimited list. */
    @@Tool.option("/wd")
    disableSpecificWarnings?: number[];

    /**
        * Disable the use vtable range checks for Control Flow Guard
        * Warning: this is an internal-use, undocumented option.  Use at your own risk.
        */
    @@Tool.option("/d2nocfgrngchk")
    disableVirtualTableChecksForControlFlowGuard?: boolean;

    /**
        * Emit unwind codes for epilog.
        * AMD64 only.
        */
    @@Tool.option("/d2epilogunwind")
    emitEpilogUnwindCodes?: boolean;

    /**
        * Enable extra GS protection for CFG
        * Warning: this is an internal-use, undocumented option.  Use at your own risk.
        */
    @@Tool.option("/d2guardgsforcfg")
    enableExtraGSProtectionForControlFlowGuard?: boolean;

    /**
        * Enables a �dispatch-mode� for Control Flow Guard checks, dispatching to target instead of returning to caller.
        * This will become enabled by default with the /guard:cf flag in the next Visual Studio update (for AMD64).
        */
    @@Tool.option("/d2guardcfgdispatch")
    enableDispatchModeForControlFlowGuard?: boolean;

    /**
        * Enables additional checks on longjmp by telling the compiler to track setjmp return addresses.
        * Warning: this is an internal-use, undocumented option.  Use at your own risk.
        */
    @@Tool.option("/d2guardlongjmp")
    enableGuardForLongJump?: boolean;

    /**
        * Enable inlined guard checks for writes and indirect calls
        * Warning: this is an internal-use, undocumented option.  Use at your own risk.
        */
    @@Tool.option("/d2guard1")
    enableInlineGuardChecks?: boolean;

    /**
        * Enable guard checks for indirect calls (aka control flow guard (CFG))
        * Warning: this is an internal-use, undocumented option.  Use at your own risk.
        */
    @@Tool.option("/d2guard4")
    enableInternalControlFlowGuard?: boolean;

    /**
        * Enable function call guard checks for writes and indirect calls
        * Warning: this is an internal-use, undocumented option.  Use at your own risk.
        */
    @@Tool.option("/d2guard2")
    enableInternalFunctionCallGuard?: boolean;

    /** Appends additional information to an error or warning message versus showing it on a separate line. */
    @@Tool.option("/WL")
    enableOneLineDiagnostics?: boolean;

    /**
        * Enable/Disable optimized debug information
        * Warning: this is an internal-use, undocumented option.  Use at your own risk.
        */
    @@Tool.option("/d2Zi+")
    enableOptimizedDebugInformation?: boolean;

    /** Enables code analysis functionality that identifies common coding defects. */
    @@Tool.option("/analyze")
    enablePreFast?: boolean;

    /**
        * Enable the return flow guard functionality
        * Warning: this is an internal-use, undocumented option.  Use at your own risk.
        */
    @@Tool.option("/d2guardss2")
    enableReturnFlowGuard?: boolean;

    /** Enforce Standard C++ meaning of 'auto' keyword. */
    @@Tool.option("/Zc:auto")
    enforceStandardAutoKeyword?: boolean;

    /** Enforce Standard C++ explicit type conversion rules. */
    @@Tool.option("/Zc:rvalueCast")
    enforceStandardConversionRules?: boolean;

    /** Enforce Standard C++ reference binding rules. */
    @@Tool.option("/Zc:referenceBinding")
    enforceStandardReferenceBinding?: boolean;

    /** Controls use of instructions found on processors that support enhanced instruction sets. */
    enhancedInstructionSet?: EnhancedInstructionSet;

    /** Controls the compiler's error reporting mode. */
    errorReporting?: Shared.ErrorReport;

    /** Controls how exceptions are handled. */
    exceptionHandling?: ExceptionHandling;

    /** Creates a listing file that has expanded attributes injected into the source file. */
    @@Tool.option("/Fx")
    expandAttributedSource?: boolean;

    /**
        * Fake version number to LKG171
        * Warning: this is an internal-use, undocumented option.  Use at your own risk.
        */
    @@Tool.option("/d1versionLKG171")
    fakeVersionNumberToLkg171?: boolean;

    /**
        * Reliable floating-point exception model.
        * When true, exceptions will be raised immediately after they are triggered.
        */
    @@Tool.option("/fp:except")
    floatingPointExceptions?: boolean;

    /** Controls the behavior of floating-point operations. */
    floatingPointModel?: FloatingPointModel;

    /** Force CodeBase strings: string literals are place in .text instead of .rdata */
    @@Tool.option("/cbstring")
    forceCodeBaseStrings?: boolean;

    /** Used to implement standard C++ behavior for the "for" statement loops with Microsoft extensions */
    @@Tool.option("/Zc:forScope")
    forceConformanceInForLoopScope?: boolean;

    /**
        * Causes the preprocessor to process the named include files.
        * This corresponds the CL's /FI command-line argument.
        */
    @@Tool.option("/FI")
    forcedIncludeFiles?: File[];

    /** Causes the preprocessor to process one or more specified #using files. */
    @@Tool.option("/FU")
    forcedUsingFiles?: File[];

    /**
        * Use pogo speculative devirtuatization even when not worth inlining
        * Warning: this is an internal-use, undocumented option.  Use at your own risk.
        */
    @@Tool.option("/d2psvcallopt", { optionSeparationMode: Tool.OptionSeparationMode.notSupported })
    forceProfileGuidedOptimizationSpeculativeDevirtualization?: number;

    /**
        * Force Disable of Vector Operations
        * Warning: this is an internal-use, undocumented option.  Use at your own risk.
        */
    @@Tool.option("/d2Qvec-")
    forceVectorOperationsDisabled?: boolean;

    /** Allows the compiler to package individual functions in the form of packaged functions (COMDATs). */
    @@Tool.option("/Gy")
    functionLevelLinking?: boolean;

    /** Enables coroutine support */
    @@Tool.option("/await")
    enableCoroutines?: boolean;

    /** Increases the number of sections that an object file can contain */
    @@Tool.option("/bigobj")
    generateExtendedObjectFormat?: boolean;

    /**
        * Specify return address validation.
        * Warning: this is an internal-use, undocumented option.  Use at your own risk.
        */
    @@Tool.option("/d2guardcheckesp[1|2]")
    generateReturnAddressValidation?: ReturnAddressValidation;

    /** Controls whether the compiler generates XML documentation files (.XDC) */
    @@Tool.option("/doc")
    generateXmlDocumentation?: boolean;

    /** Add control flow guard security checks (/guard:cf CL.exe option). */
    @@Tool.option("/guard:cf[+|-]")
    guardControlFlow?: boolean;

    /** Enables implicit noexcept on user-defined destructors and deallocators as well as compiler-defined special member functions. */
    @@Tool.option("/Zc:implicitNoexcept", { negateOption: true })
    implicitNoExcept?: boolean;

    /** When true, static local variable initialization is thread-safe. */
    @@Tool.option("/Zc:threadSafeInit", { negateOption: true })
    threadSafeLocalStaticInitialization?: boolean;

    /** When true, sized global deallocation is allowed. */
    @@Tool.option("/Zc:sizedDealloc")
    permitSizedDeallocation?: boolean;

    /**
        * IgnorePragmaWarningError deactivates #pragma warning(error:..) and /WX
        * Warning: this is an internal-use, undocumented option.  Use at your own risk.
        */
    @@Tool.option("/d1ignorePragmaWarningError")
    ignorePragmaWarningError?: boolean;

    /** Prevents the compiler from searching for include files in directories specified in the PATH and INCLUDE environment variables. */
    @@Tool.option("/X")
    ignoreStandardIncludePath?: boolean;

    /**
     * This argument allows you to pass two things:
     * File: the runners can take files that are in or under the BuildXL spec file directory.
     * StaticDirectory: the runner can take a sealed directory with a bunch of includes.
     * See http://msdn.microsoft.com/en-us/library/36k2cdd4.aspx for more information.This also works for #import.
     * Directories that are searched for include files.
     */
    includes?: (File | StaticDirectory)[];

    /** Creates a binary that can be executed in the Windows kernel. */
    @@Tool.option("/kernel")
    kernelModeBinary?: boolean;

    /** Specify the version of the language standard to use. */
    languageVersion?: LanguageVersion;

    /** Controls whether managed code is produced. */
    managedCompilation?: ManagedCompilation;

    /** Enables minimal rebuild, which determines whether C++ source files that include changed C++ class definitions (stored in header (.h) files) need to be recompiled. */
    @@Tool.option("/Gm[+|-]")
    minimalRebuild?: boolean;

    /** If true, omits the default C run-time library name from the object (.obj) file. By default, the compiler puts the name of the library into the .obj file to direct the linker to the correct library. */
    @@Tool.option("/Zl")
    omitDefaultLibraryName?: boolean;

    /** Causes the compiler to process #pragma omp. */
    @@Tool.option("/openmp")
    openMPSupport?: boolean;

    /** Controls the overall level of optimization. */
    optimizations?: Optimizations;

    /** Don't strip comments in preprocessors output. */
    @@Tool.option("/C")
    preprocessKeepComments?: boolean;

    /** Specify the directory for preprocessor output. */
    @@Tool.option("/Fi[directory]")
    preprocessorOutputDirectory?: Path;

    /**
        * Route preprocessor output to a file
        * The output file is foo.i for a source file of foo.c
        */
    @@Tool.option("/P")
    preprocessorOutputToAFile?: boolean;

    /**
        * Route preprocessor output to stdout
        * Preprocesses C and C++ source files and copies the preprocessed
        * files to the standard output device. /E provides #line directives
        * so that errors refer to line numbers of the original source file.
        * /EP does not provide the #line directives so that errors refer
        * to line numbers of the preprocessed file. The /EP and /E options
        * both suppresses compilation.
        */
    preprocessorStandardOutputRouting?: PreprocessorStandardOutputRouting;

    /** Specifies a list of one or more preprocessing symbols. */
    @@Tool.option("/D")
    preprocessorSymbols?: Shared.PreprocessorSymbol[];

    /**
        * Remove prefix from embedded __FILE__ string matches.
        * Warning: this is an internal-use, undocumented option.  Use at your own risk.
        */
    @@Tool.option("/d1trimfile", { optionSeparationMode: Tool.OptionSeparationMode.supported, optionSeparator: ":" })
    removePrefixFromFileStrings?: Path[];

    /** Remove unreferenced function or data if it is COMDAT or has internal linkage only */
    @@Tool.option("/Zc:inline")
    removeUnreferencedCodeData?: boolean;

    /**
     * Add a response file with a set of common defines.
     * This response file will be added at the start of the command line.
     */
    @@Tool.option("@{responseFile}")
    responseFile?: File;

    /** Enable checking for conversion to smaller types, incompatible with any optimization type other than debug. */
    runtimeChecks?: RuntimeChecks;

    /** Controls the runtime library that supports the generated code. */
    runtimeLibrary?: RuntimeLibrary;

    /**
        * Declare runtime library support for vector operations.
        * This corresponds to the opposite of CL's /d2Qvec-mathlib- command-line argument.
        * Warning: this is an internal-use, undocumented option.  Use at your own risk.
        */
    @@Tool.option("/d2Qvec-mathlib", { negateOption: true })
    runtimeLibrarySupportForVectorOperations?: boolean;

    /** Adds code for checking C++ object types at run time. */
    @@Tool.option("/GR[+|-]")
    runtimeTypeInfo?: boolean;

    /** Enable additional Security Development Lifecycle (SDL) recommended checks; includes enabling additional secure code generation features and enables extra security-relevant warnings as errors. */
    @@Tool.option("/sdl")
    sdlCheck?: boolean;

    /**
        * Specify what version of mscorlib we should use.
        *   - Mscorlib version:   Determine what version of mscorlib we should use
        *   - nomscorlib:         User provided
        *   - nostdlib:           Don't look in COR system directory for mscorlib.dll
        *   - nopurecrt:          Turn OFF pure MSIL CRT
        *   - oldCustomModifiers: Use old custom modifiers
        *
        * Warning: this is an internal-use, undocumented option.  Use at your own risk.
        */
    setCoreLibraryVersion?: CoreLibraryVersion;

    /** If true, causes the compiler to output a list of the include files. */
    @@Tool.option("/showIncludes")
    showIncludes?: boolean;

    /** List of warnings that should only be issued once even if multiple occurrences exist. */
    @@Tool.option("/wo")
    singleIssueWarnings?: number[];

    /** Disable warnings introduced after this version. */
    @@Tool.option("/Wv", { optionSeparationMode: Tool.OptionSeparationMode.required, optionSeparator: ":" })
    disableWarningsAfterVersion?: string;

    /** Controls stack checking calls. */
    @@Tool.option("/Gs")
    stackCheckingThreshold?: number;

    /** Disable string-literal to [char|wchar_t]* conversion. */
    @@Tool.option("/Zc:stringStrings")
    strictStrings?: boolean;

    /** Enforce strict standards conformance. */
    @@Tool.option("/permissive-")
    strictStandardsConformance?: boolean;

    /** Controls the boundary for struct member alignment. */
    structMemberAlignment?: StructMemberAlignment;

    /**
        * Suppress new warnings after LKG171.
        *
        * Warning: this is an internal-use, undocumented option.  Use at your own risk.
        */
    @@Tool.option("/d1warningLKG171")
    suppressPostLkg171Warnings?: boolean;

    /** Suppresses the display of the sign-on banner when the compiler starts up and display of informational messages during compilation. */
    @@Tool.option("/nologo")
    suppressStartupBanner?: boolean;

    /** Assume operator new throws on failure. */
    @@Tool.option("/Zc:throwingNew")
    throwingNew?: boolean;

    /** Treats the specified list of compiler warnings as errors. */
    @@Tool.option("/we")
    treatSpecificWarningsAsErrors?: number[];

    /** Treats all compiler warnings as errors. */
    @@Tool.option("/WX")
    treatWarningAsError?: boolean;

    /** When true, the type wchar_t becomes a native type that maps to __wchar_t in the same way that short maps to __int16. */
    @@Tool.option("/Zc:wchar_t")
    treatWchartAsBuiltInType?: boolean;

    /** Undefine all previously defined preprocessor values. */
    @@Tool.option("/u")
    undefineAllPreprocessorSymbols?: boolean;

    /** Specifies a list of one or more preprocessor symbols to undefine. */
    @@Tool.option("/U")
    undefinePreprocessorSymbols?: string[];

    /** Use full paths in diagnostic messages. */
    @@Tool.option("/FC")
    useFullPaths?: boolean;

    /**
        * Use the registry for #import
        * When set, the compiler with load the OLE type library from the location in the registry.
        * Which means that the component must be registered on the building machine.
        *
        * Warning: this is an internal-use, undocumented option.  Use at your own risk.
        */
    @@Tool.option("/d1import_no_registry", { negateOption: true })
    useRegistryForImport?: boolean;

    /**
        * This corresponds to CL's /d1uselinevar command-line argument.
        * Warning: this is an internal-use, undocumented option.  Use at your own risk.
        */
    @@Tool.option("/d1uselinevar")
    useLineVariable?: boolean;

    /** Controls whether the assembler listing file is produced in ASCII or UTF-8. */
    @@Tool.option("/FAu")
    useUnicodeForAssemblerListing?: boolean;

    /** Specifies how the volatile keyword is interpreted. */
    volatile?: VolatileInterpretation;

    /** Determines how strict the compiler should be about warnings. */
    warningLevel?: ClWarningLevel;

    /**
        * Specifies the warning levels for particular warnings.
        * This will be passed to the command line in the form:
        * /w[key][value[i]] for each key (representing warning level 1-4) and all its values (representing warnings).
        * (e.g. /w14464 to set C4464 to have warning level 1)
        */
    @@Tool.option("/wln")
    warningsToLevelMap?: Map<ClWarningLevel, number[]>; //TODO: check if we can have enums as keys in DScript

    /**
        * Generate warnings for usage of global variables.
        * This corresponds to CL's /d1WarnOnGlobals command-line argument.
        * Warning: this is an internal-use, undocumented option.  Use at your own risk.
        */
    @@Tool.option("/d1WarnOnGlobals")
    warnOnGlobals?: boolean;

    /** Tells the compiler not to use default Windows.winmd. */
    @@Tool.option("/ZW:nostdlib")
    winRTNoStdLib?: boolean;

    /**
        * Guarantees C/C++ deterministic binaries and hides /d2threads:1 for different scenarios.
        */
    @@Tool.option("/brepro")
    compilerDeterminism?: boolean;

    /**
        * Use SHA256 hash algorithm for source file checksums that get stored in the PDB.
        */
    @@Tool.option("/ZH:SHA_256")
    useSha256ForChecksum?: boolean;
}

/**
    * The value produced by ClRunner.
    */
@@public
export interface Result {
    /**
        * The result of processing each compiland.
        * If a precompiled header was created, its compiland is accessible through
        * property rather than this one.
        */
    compilationOutputs?: Map<string, CompilationOutput>;

    /**
        * The PCH file generated during precompiled header compilation.
        * Only valid if
        * 1. PrecompiledHeaderName input argument was valid
        * 2. precompiled header creation succeeded
        * 3. PCH file not in sources
        */
    precompiledHeader?: CompilationOutput;
}

/**
    * Specifies the contents of assembly language output files.
    */
@@public
export const enum AssemblerOutput {
    /** Assembly-only. */
    @@Tool.option("/FA")
    assemblyCode,
    /** Assembly with machine code. */
    @@Tool.option("/FAc")
    assemblyAndMachineCode,
    /** Assembly with source code. */
    @@Tool.option("/FAs")
    assemblyAndSourceCode,
    /** Assembly with source code and machine code. */
    @@Tool.option("/FAcs")
    all
}

/**
    * Determines default parameter-passing calling conventions.
    */
@@public
export const enum CallingConvention {
    /** Specifies the __cdecl calling convention for all functions except C++ member functions and functions marked __stdcall, __fastcall, or __vectorcall. */
    @@Tool.option("/Gd")
    cdecl,
    /** Specifies the __fastcall calling convention for all functions except C++ member functions and functions marked __cdecl, __stdcall, or __vectorcall. All __fastcall functions must have prototypes. */
    @@Tool.option("/Gr")
    fastCall,
    /** Specifies the __stdcall calling convention for all functions except C++ member functions and functions marked __cdecl, __fastcall, or __vectorcall. All __stdcall functions must have prototypes. */
    @@Tool.option("/Gz")
    stdCall,
    /** Specifies the __vectorcall calling convention for all functions except C++ member functions and functions marked __cdecl, __fastcall, or __stdcall. All __vectorcall functions must have prototypes. */
    @@Tool.option("/Gv")
    vectorCall,
}

/**
    * Determines how strict the compiler should be about warnings.
    */
@@public
export const enum ClWarningLevel {
    /** Enables all warnings, including those disabled by default. */
    @@Tool.option("/Wall")
    enableAllWarnings,
    /** Displays severe warning messages. */
    @@Tool.option("/W1")
    level1,
    /** Displays level 1 warnings plus some less-severe warnings. */
    @@Tool.option("/W2")
    level2,
    /**
     * Displays level 2 warnings plus certain less-severe warnings.
     * This level is recommendeded for production purposes.
     */
    @@Tool.option("/W3")
    level3,
    /** Displays all level 3 warnings plus informational warnings. */
    @@Tool.option("/W4")
    level4,
    /** Turns off emission of all warning messages. */
    @@Tool.option("/W0")
    turnOffAllWarnings
}

/**
    * A value carrying the various artifacts produced as the result of compiling a single file.
    */
@@public
export interface CompilationOutput {
    /** The primary output of the C/C++ compiler. */
    binary: Shared.BinaryObject;

    /** Optional XML file with code analysis results (if PREfast is enabled) */
    analysisResult: File;

    /** Optional assembler listing file. */
    assemblerListing?: File;

    /** Optional browsing information file enabling IntelliSense. */
    browseInformation?: File;

    /** The optional XML documentation file. */
    xmlDocumentation?: File;
}

/** Specify source file type for compilation */
@@public
export const enum CompilationType {
    /** The source file is C */
    @@Tool.option("/TC")
    compileAsC,
    /** The source file is CPP */
    @@Tool.option("/TP")
    compileAsCpp
}

/** Mscorlib version selection. */
@@public
export const enum CoreLibraryVersion {
    /** Don't look in COR system directory for mscorlib.dll */
    @@Tool.option("/d1clr:nostdlib")
    skipCoreSystemDirectory,
    /** User provided version of mscorlib - don't add an explict #using for mscorlib.dll */
    @@Tool.option("/d1clr:nopurecrt")
    turnoffPureMsilRuntimeLibrary,
    /** Custom version of mscorlib - don't add an explict #using for mscorlib.dll */
    @@Tool.option("/d1clr:nomscorlib")
    useCustomLibrary,
    /** Use old custom modifiers TO */
    @@Tool.option("/d1clr:oldCustomModifiers")
    useOldCustomModifiers
}

/** Specifies the type of debugging information generated by the C/C++ compiler. */
@@public
export const enum DebugInformationFormat {
    /**
     * Produces a program database in a format that supports the Edit and Continue feature.
     * The symbolic debugging information includes the names and types of variables, as well as functions and line numbers. "/>
     * This corresponds to CL's /ZI command-line argument.
     */
    @@Tool.option("/ZI")
    editAndContinue,
    /** Produces no debugging information, so compilation may be faster. */
    none,
    /** 
     * C7 compatible format.
     * This corresponds to CL's /Z7 command-line argument.
     */
    @@Tool.option("/Z7")
    oldStyle,
    /**
     * Produces a program database (PDB) that contains type information and symbolic debugging information for use with the debugger.
     * The symbolic debugging information includes the names and types of variables, as well as functions and line numbers. "/>
     * This corresponds to CL's /Zi command-line argument.
     */
    @@Tool.option("/Zi")
    programDatabase
}

/** Controls use of instructions found on processors that support enhanced instruction sets. */
@@public
export const enum EnhancedInstructionSet {
    /** Advanced vector extensions. */
    @@Tool.option("/arch:AVX")
    advancedVectorExtensions,
    /** Advanced vector extensions 2. */
    @@Tool.option("/arch:AVX2")
    advancedVectorExtensions2,
    /** Enable use of VFPv4 instructions */
    @@Tool.option("/arch:VFPv4")
    armExtensionVFPv4,
    /** Enable use of ARMv7VE instructions */
    @@Tool.option("/arch:ARMv7VE")
    armVectorExtension7VE,
    /** ARM Vector Floating Point Extension Version 3 with 16 double precision registers */
    @@Tool.option("/arch:VFPv3-D16")
    armVectorFloatingPointExtensionV3D16,
    /** x86: Use no enhanced instructions and use x87 for floating point */
    @@Tool.option("/arch:IA32")
    noExtensions,
    /** Streaming SIMD extensions. */
    @@Tool.option("/arch:SSE")
    streamingSimdExtensions,
    /** Streaming SIMD extensions 2. */
    @@Tool.option("/arch:SSE2")
    streamingSimdExtensions2
}

/** Controls how exceptions are handled. */
@@public
export const enum ExceptionHandling {
    /** The exception-handling model that catches asynchronous (structured) and synchronous (C++) exceptions. */
    @@Tool.option("/EHa")
    async,
    /** The exception-handling model that catches C++ exceptions only and tells the compiler to assume that extern C functions never throw a C++ exception. */
    @@Tool.option("/EHsc")
    sync,
    /** The exception-handling model that catches C++ exceptions only and tells the compiler to assume that extern C functions do throw an exception. */
    @@Tool.option("/EHs")
    syncCThrow
}

/** Allows optimizations for a specific architecture or for the specifics of micro-architectures in the AMD and the Intel architectures. */
@@public
export const enum FavorProcessor {
    /**
     * Produces the code that is optimized for the specifics of micro-architectures in the AMD and the Intel architectures.
     * (x86 and x64)
     * While /favor:blend may not give the best performance possible on a specific processor, 
     * it is designed to give the best performance across a broad range of x86 and x64 processors.
     */
    @@Tool.option("/favor:blend")
    blend = 0x0,
    /**
     * Produces the code that is optimized for the specifics of the Intel Atom and Intel Centrino Atom
     * (x86 and x64)
     * Code that is generated by using /favor:ATOM may also produce Intel SSSE3, SSE3, SSE2, and SSE instructions for Intel processors.
     */
    @@Tool.option("/favor:ATOM")
    atom,
    /**
     * Optimizes the generated code for the AMD Opteron, and Athlon processors that support 64-bit extensions.
     * (x64 only)
     * The optimized code can run on all x64 compatible platforms. Code that is generated by using 
     * /favor:AMD64 might cause worse performance on Intel processors that support Intel64.
     */
    @@Tool.option("/favor:AMD64")
    amd64,
    /**
     * Optimizes the generated code for Intel processors that support Intel64.
     * (x64 only)
     * The resulting code can run on any x64 platform. Code that is generated with 
     * /favor:INTEL64 might cause worse performance on AMD Opteron, and Athlon processors that support 64-bit extensions.
     */
    @@Tool.option("/favor:INTEL64")
    intel64
}

/** Controls the behavior of floating-point operations. */
@@public
export const enum FloatingPointModel {
    /**
     * Creates the fastest code in the majority of cases.
     * When you log in with administrator privileges, a pop-up window is displayed and allows you to report any failures since the last time you were logged in
     * (you will not be prompted to send reports for failures more than once every three days).
     */
    @@Tool.option("/fp:fast")
    fast,
    /** Improves the consistency of floating-point tests for equality and inequality. */
    @@Tool.option("/fp:precise")
    precise,
    /** The strictest floating-point model. */
    @@Tool.option("/fp:strict")
    strict
}

/** Controls how function inlining behaves. */
@@public
export const enum InlineFunctionExpansion {
    /**
     * Expands functions marked as inline or __inline and any other function that the compiler chooses.
     * Expansion occurs at the compiler's discretion, often referred to as auto-inlining.
     */
    @@Tool.option("/Ob2")
    anySuitable,
    /** Disables function inlining. */
    @@Tool.option("/Ob0")
    disabled,
    /** Expands only functions marked as inline, __inline, __forceinline or __inline or, in a C++ member function, defined within a class declaration. */
    @@Tool.option("/Ob1")
    onlyExplicitInline
}

/** Controls the version of the language standard to compile for. */
@@public
export const enum LanguageVersion {
    /** C++14 (ISO/IEC 14882:2014). */
    @@Tool.option("/std:c++14")
    cpp14,
    /** C++17 (ISO/IEC 14882:2017). */
    @@Tool.option("/std:c++17")
    cpp17,
    /** latest C++ draft standard (feature set subject to change). */
    @@Tool.option("/std:c++latest")
    cppLatest,
}

/** Controls the production of managed code. */
@@public
export const enum ManagedCompilation {
    /** Managed code is not produced. */
    disabled,
    /** Creates metadata for your application that can be consumed by other CLR applications, and allows your application to consume types and data in the metadata of other CLR components. */
    @@Tool.option("/clr")
    mixed,
    /** Produces an MSIL-only output file with no native executable code, although it can contain native types compiled to MSIL. */
    @@Tool.option("/clr:pure")
    pure,
    /** Produces an MSIL-only and verifiable output file with no native executable code. */
    @@Tool.option("/clr:safe")
    safe
}

/** 
 * Controls how the compiler optimizes code.
 */
@@public
export const enum OptimizationMode {
    /** Disable optimizations */
    @@Tool.option("/Od")
    disabled,
    /** Optimize for smaller code */
    @@Tool.option("/O1")
    minimizeSpace,
    /** Optimize for faster speed */
    @@Tool.option("/O2")
    maximizeSpeed,
    /** 
     * Maximum "safe" optimization
     * Equivalent to 'maximizeSpeed', except that it doesn't enable function-level linking (/Gy) or string folding (/GF) by default. 
     */
    @@Tool.option("/Ox")
    maximumSafe
}

/**
 * Controls the various optimization settings for the C/C++ compiler.
 * The overall optimization level is controlled by the optimizationMode property. Setting this property also provides default values
 * for the remaining properties whose values are not explicitly specified. For example, setting optimizationMode to maximizeSpeed
 * also implies optimizeFramePointer: true, intrinsicFunctions: true, stringPooling: true, and functionLevelLinking: true. Setting
 * the individual properties explicitly overrides the default behavior provided by the specified optimizationMode setting.  
 */
@@Tool.option("/O", { supportsMultipleValues: true })
@@Tool.option("/G")
@@public
export interface Optimizations {
    /** Controls the overall level of optimization applied to the code */
    optimizationMode?: OptimizationMode;
    /**
    * Favor optimizations for a specific architecture or for the specifics of micro-architectures
    */
    favorProcessor?: FavorProcessor;
    /** Enable frame pointer omission */
    @@Tool.option("/Oy")
    optimizeFramePointer?: boolean;
    /** Enable intrinsic functions */
    @@Tool.option("/Oi")
    intrinsicFunctions?: boolean;
    /** Enables the compiler to create a single read-only copy of identical strings in the program image and in memory during execution, resulting in smaller programs. */
    @@Tool.option("/GF")
    stringPooling?: boolean;
    /**
     * Allows the compiler to package individual functions in the form of packaged functions (COMDATs).
     * Function-level linking is required for edit and continue to work.
     */
    @@Tool.option("/Gy")
    functionLevelLinking?: boolean;
    /** Package global data in COMDAT sections for optimization. */
    @@Tool.option("/Gw")
    optimizeGlobalData?: boolean;
    /** Controls the level of function inlining. */
    inlineFunctionExpansion?: InlineFunctionExpansion;
    /** Enables cross-module optimizations by delaying code generation to link time */
    @@Tool.option("/GL")
    wholeProgram?: boolean;
    /** Results in more efficient code for an .exe file for accessing thread-local storage (TLS) variables. */
    @@Tool.option("/GA")
    optimizeThreadLocalStorageAccessForExe?: boolean;
    /** Disables thread local storage optimizations that are unsafe in the presence of fibers. */
    @@Tool.option("/GT")
    fiberSafeThreadLocalStorage?: boolean;
}

/** Controls the creation or use of a precompiled header file. */
@@public
export const enum PrecompiledHeader {
    /** Precompiled headers aren't being used. */
    disabled,
    /** Instructs the compiler to create a precompiled header (.pch) file that represents the state of compilation at a certain point. */
    @@Tool.option("/Yc")
    create,
    /** Instructs the compiler to use an existing precompiled header file in the current compilation. */
    @@Tool.option("/Yu")
    use
}

/** Specify possible stdout routing for preprocessor output. */
@@public
export const enum PreprocessorStandardOutputRouting {
    /** No routing to stdout specified. */
    noStandardOutputRouting,
    /**
     * Route to stdout with #line directives
     * MSDN:
     * /E adds #line directives to the output at the beginning and end
     * of each included file and around lines removed by preprocessor
     * directives for conditional compilation. These directives renumber
     * the lines of the preprocessed file. As a result, errors generated
     * during later stages of processing refer to the line numbers of
     * the original source file rather than lines in the preprocessed file.
     */
    @@Tool.option("/E")
    standardOutputWithLineDirectives,
    /**
     * Route to stdout with no #line directives
     * MSDN:
     * The /EP option suppresses compilation. Errors generated during later 
     * stages of processing refer to the line numbers of the preprocessed
     * file rather than the original source file. If you want line numbers
     * to refer to the original source file, use /E (Preprocess to stdout)
     * instead.
     */
    @@Tool.option("/EP")
    standardOutputWithNoLineDirectives
}

/** Specify the type of return address validation (x86 only). */
@@public
export const enum ReturnAddressValidation {
    /** Don't validate the return address. */
    noValidation,
    /** Validate the return address in the ESP register and the return address on the stack. */
    validateReturnAddress,
    /** Validate the return address in the ESP register. */
    validateReturnAddressAndStackPointer
}

/** Controls the run-time error checks feature. */
@@Tool.option("/RTC", { supportsMultipleValues: true })
@@public
export const enum RuntimeChecks {
    /** TODO: This is a workaround. The TypeChecker as of the current version doesn't support comparisons between numbers and enums so a 0 value is needed for flags */
    empty = 0x0,
    /**
     * Reports when a value is assigned to a smaller data type and results in a data loss. For example, if a value of type short 0x101 
     * is assigned to a variable of type char.
     */
    @@Tool.option("/RTCc")
    reportDataLossOnTruncation = 0x1,
    /** 
     * Enables stack frame run-time error checking.
     * Initialization of local variables to a nonzero value. This helps identify bugs that do not appear when running in debug mode. 
     * There is a greater chance that stack variables will still be zero in a debug build compared to a release build because of 
     * compiler optimizations of stack variables in a release build. Once a program has used an area of its stack, it is never reset 
     * to 0 by the compiler. Therefore, subsequent, uninitialized stack variables that happen to use the same stack area can return 
     * values left over from the prior use of this stack memory.
     * Detection of overruns and underruns of local variables such as arrays. /RTCs will not detect overruns when accessing memory 
     * that results from compiler padding within a structure. Padding could occur by using align (C++), /Zp (Struct Member Alignment), 
     * or pack, or if you order structure elements in such a way as to require the compiler to add padding.
     * Stack pointer verification, which detects stack pointer corruption. Stack pointer corruption can be caused by a calling 
     * convention mismatch. For example, using a function pointer, you call a function in a DLL that is exported as __stdcall 
     * but you declare the pointer to the function as __cdecl.
     */
    @@Tool.option("/RTCs")
    stackFrameRuntime = 0x2,
    /**
     * Reports when a variable is used without having been initialized.
     * For example, an instruction that generates C4701 may also generate a run-time error under /RTCu. Any instruction that 
     * generates Compiler Warning (level 1 and level 4) C4700 will generate a run-time error under /RTCu.
     */
    @@Tool.option("/RTCu")
    uninitializedLocalUsage = 0x4
}

/** Controls the runtime library that supports the generated code. */
@@public
export const enum RuntimeLibrary {
    /**
     * Makes the code use the multithreaded static version of the run-time library.
     * This defines _MT and adds an automatic dependency on LIBCMT.LIB (via /DEFAULTLIB:LIBCMT.LIB to linker directives).
     */
    @@Tool.option("/MT")
    multithreaded,
    /**
     * Makes the code use the multithreaded static debug version of the run-time library.
     * This defines _MT and _DEBUG. It then adds an automatic dependency on LIBCMTD.LIB (via /DEFAULTLIB:LIBCMTD.LIB to linker directives).
     */
    @@Tool.option("/MTd")
    multithreadedDebug,
    /**
     * Makes the code use the multithreaded DLL version of the run-time library.
     * This defines _MT and _DLL. It then adds an automatic dependency on MSVCRT.LIB (via /DEFAULTLIB:MSVCRT.LIB to linker directives).
     */
    @@Tool.option("/MD")
    multithreadedDebugDll,
    /**
     * Makes the code use the multithreaded DLL debug version of the run-time library.
     * This defines _MT, _DLL and _DEBUG. It then adds an automatic dependency on MSVCRTD.LIB (via /DEFAULTLIB:MSVCRTD.LIB to linker directives).
     */
    @@Tool.option("/MDd")
    multithreadedDll
}

/** Controls the boundary for struct member alignment. */
@@public
export const enum StructMemberAlignment {
    /** Packs structures on 1-byte boundaries. */
    @@Tool.option("/Zp1")
    align1,
    /** Packs structures on 2-byte boundaries. */
    @@Tool.option("/Zp2")
    align2,
    /** Packs structures on 4-byte boundaries. */
    @@Tool.option("/Zp4")
    align4,
    /** Packs structures on 8-byte boundaries. */
    @@Tool.option("/Zp8")
    align8,
    /** Packs structures on 16-byte boundaries. */
    @@Tool.option("/Zp16")
    align16
}

/** Specifies how the volatile keyword is interpreted. */
@@public
export const enum VolatileInterpretation {
    /**
     * ISO-standard C++ language semantics.
     * MSDN:
     * Selects strict volatile semantics as defined by the ISO-standard
     * C++ language. Acquire/release semantics are not guaranteed on 
     * volatile accesses. If the compiler targets ARM, this is the 
     * default interpretation of volatile. 
     */
    @@Tool.option("/volatile:iso")
    iso,
    /**
     * Microsoft extended semantics
     * MSDN:
     * Selects Microsoft extended volatile semantics, which add memory 
     * ordering guarantees beyond the ISO-standard C++ language. 
     * Acquire/release semantics are guaranteed on volatile accesses. 
     * However, this option also forces the compiler to generate hardware
     * memory barriers, which might add significant overhead on ARM and
     * other weak memory-ordering architectures. If the compiler targets
     * any platform except ARM, this is default interpretation of volatile.
     */
    @@Tool.option("/volatile:ms")
    ms
}
