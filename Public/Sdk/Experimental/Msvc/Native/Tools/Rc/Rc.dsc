// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

import {Shared, PlatformDependentQualifier} from "Sdk.Native.Shared";

export declare const qualifier: PlatformDependentQualifier;

/**
 * Localization comment extraction options.
 */
@@public
export const enum LocalizationCommentExtractionOptions {
    none = 0,

    @@Tool.option("/tm")
    suppressMnemonics = 1,

    @@Tool.option("/tc")
    suppressComments = 2,

    @@Tool.option("/te")
    warningsAsErrors = 4,

    @@Tool.option("/ti")
    saveSourceInformation = 8,

    @@Tool.option("/tw")
    noExternalComments = 16,

    @@Tool.option("/ta")
    allResources = 32,
}

/**
 * Arguments passed to the RcRunner
 */
@@public
export interface Arguments extends Transformer.RunnerArguments {
    /**
     * Additional literal options to pass to rc.exe.
     * NOTE: These should contain literal strings (no paths). This is a temporary API until all options are documented.
     */
    additionalOptions?: string[];

    /** The code page used for NLS conversions. Default value (0) used to indicate absence of the option. */
    @@Tool.option("/c", {optionSeparationMode: Tool.OptionSeparationMode.supported, optionSeparator: " "})
    characterConversionCodePage?: number;

    /** Specify the custom file version for checksum in MUI creation */
    @@Tool.option("/g2")
    customFileVersionForChecksum?: string;

    /** Specifies the default language using the language identifier. */
    @@Tool.option("/l", {optionSeparationMode: Tool.OptionSeparationMode.supported, optionSeparator: " "})
    defaultLanguageIdentifier?: string;

    /** Specifies the default language using the language name. */
    @@Tool.option("/ln")
    defaultLanguageName?: string;

    /**
     * Whether or not to emit a language-dependent (MUI) .RES file
     * If true, RC generates a MUI file if the only localizable resource being included in the MUI file is a version resource.If false, RC will not generate a MUI file if the only localizable resource being included in the MUI file is a version resource.
     */
    @@Tool.option("/g1")
    emitLanguageDependentResourceFile?: boolean;

    /** Generate an .lce file which will contain the extracted resource commenting data. */
    @@Tool.option("/tn", {optionSeparationMode: Tool.OptionSeparationMode.supported, optionSeparator: " "})
    emitLocalizationCommentExtractionFile?: boolean;

    /** Emit .RES file */
    @@Tool.option("/r")
    emitResourceFile?: boolean;

    /** Append NULLs to all strings in the string tables */
    @@Tool.option("/n")
    generateNullTerminatedStrings?: boolean;

    /** Ignore INCLUDE environment variable */
    @@Tool.option("/x")
    ignoreIncludeEnvironmentVariable?: boolean;

    /**
     * This argument allows you to pass two things:
     * File: the runners can take files that are in or under the BuildXL spec file directory.
     * StaticDirectory: the runner can take a sealed directory with a bunch of includes.
     */
    @@Tool.option("/I")
    includes?: (File | StaticDirectory)[];

    /**
     * Localization comment extraction options.
     * If EmitLocalizationCommentExtractionFile is true, then these options control the data written.
     */
    localizationCommentExtractionOptions?: LocalizationCommentExtractionOptions;

    /** Defines symbols for the preprocessor that may be tested with the #ifdef directive. */
    @@Tool.option("/d")
    preprocessorSymbols?: Shared.PreprocessorSymbol[];

    /** The RC Configuration file for the resource only DLL */
    @@Tool.option("/q", {optionSeparationMode: Tool.OptionSeparationMode.supported, optionSeparator: " "})
    rcConfigurationFile?: File;

    /** The name for the generated .RES file */
    @@Tool.option("/fo")
    resourceFileName?: PathAtom;

    /** A list of all the resources used by the runner */
    resources?: File[];

    /** Specifies the resource string length limit as a percentage of MAXSTR */
    @@Tool.option("/sl")
    resourceStringLengthLimitPercentage?: number;

    /** The input .RC files */
    sources: Shared.SourceFileArtifact[];

    /** Specify the substitute font name */
    @@Tool.option("/z", {optionSeparationMode: Tool.OptionSeparationMode.supported, optionSeparator: " "})
    substituteFontName?: string;

    /** Suppress the start-up informational display  as well as informational messages during compilation. */
    @@Tool.option("/nologo")
    suppressStartupBanner?: boolean;

    /** Specifies the ultimate fallback language using language identifier. */
    @@Tool.option("/g")
    ultimateFallbackLanguageIdentifier?: string;

    /** Specifies the ultimate fallback language using language name */
    @@Tool.option("/gn")
    ultimateFallbackLanguageName?: string;

    /** Undefines symbols for the preprocessor that may be tested with the #ifdef directive. */
    @@Tool.option("/u")
    undefinePreprocessorSymbols?: string[];

    /** Display progress messages */
    @@Tool.option("/v")
    verbose?: boolean;

    /** Do not warn if there are duplicate control Ids */
    @@Tool.option("/y")
    warnOnDuplicateControlIds?: boolean;

    /** Warn on invalid code page in .rc (default is an error) */
    @@Tool.option("/w")
    warnOnInvalidCodePage?: boolean;
}

/**
 * The value produced by the Rc Transformer
 */
@@public
export interface RcOutput {
    /** The generated localized content extraction file */
    lceFile: File;

    /** The language-dependent .RES file generated */
    multilingualFile: File;

    /** The .RES file generated from the rc file */
    resFile: File;
}

export const defaultArguments: Arguments = {
    characterConversionCodePage: 0,
    emitLanguageDependentResourceFile: false,
    emitLocalizationCommentExtractionFile: false,
    emitResourceFile: false,
    generateNullTerminatedStrings: false,
    localizationCommentExtractionOptions: LocalizationCommentExtractionOptions.allResources,
    resourceStringLengthLimitPercentage: 0,
    sources: undefined,
    suppressStartupBanner: true,
    verbose: false,
    warnOnDuplicateControlIds: true,
    warnOnInvalidCodePage: false,
};

export function defaultTool(): Transformer.ToolDefinition {
    //TODO: Need to have a downloadable  package for the Windows SDk.
    Contract.fail("No default tool was provided");
    return undefined;
}

/**
 * Determines if the file is an rc file
 */
@@public
export function isRcFile(source: Shared.SourceFileArtifact): boolean {
    return typeof source === "File" && (source as File).extension === a`.rc`;
}

/**
 * Resource Compiler runner (RC.exe) generates separate resource files for applications that have both language-neutral and
 * language-specific resources. The resource compiler is a tool used in building Windows-based applications that compiles
 * the application's resources.
 */
@@Tool.runner("rc.exe")
@@public
export function evaluate(args: Arguments) : Map<PathAtom, RcOutput> {
    args = defaultArguments.override<Arguments>(args);

    let includes          = (args.includes === undefined) ? [] : args.includes.mapDefined(include => typeof include === "File" ? include as File : undefined);
    let includeSearchDirs = (args.includes === undefined) ? [] : args.includes.mapDefined(include => typeof include !== "File" ? include as StaticDirectory : undefined);

    let outputArray = args.sources.map(s => <[PathAtom, RcOutput]>[Shared.getFile(s).name, evaluateOne(args, Shared.getFile(s), includes, includeSearchDirs)]);
    return Map.empty<PathAtom, RcOutput>().addRange(...outputArray);
}

function evaluateOne(args: Arguments, source: File, includes: File[], includeSearchDirs: StaticDirectory[]) : RcOutput {
    let workingDirectory = Context.getSpecFileDirectory();
    let ext = PathAtom.create(".res");
    let sourceName = source.name;
    let outputResFileName = args.resourceFileName || sourceName.changeExtension(ext);
    let outputDirectory = Context.getNewOutputDirectory("rc");
    let outputResFile = outputDirectory.combine(outputResFileName);
    let outputMresFile = args.emitLanguageDependentResourceFile ? outputDirectory.combine(sourceName.changeExtension(".muires")) : undefined;
    let outputLceFile = args.emitLocalizationCommentExtractionFile ? outputDirectory.combine(sourceName.changeExtension(".lce")) : undefined;

    let cmdArgs: Argument[] = [
        Cmd.flag("/nologo", args.suppressStartupBanner),
        Cmd.args(args.additionalOptions),
        ...(args.emitLocalizationCommentExtractionFile ? [
            Cmd.flag("/ta", hasFlag(args.localizationCommentExtractionOptions, LocalizationCommentExtractionOptions.allResources)),
            Cmd.flag("/te", hasFlag(args.localizationCommentExtractionOptions, LocalizationCommentExtractionOptions.warningsAsErrors)),
            Cmd.flag("/tw", hasFlag(args.localizationCommentExtractionOptions, LocalizationCommentExtractionOptions.noExternalComments)),
            Cmd.flag("/ti", hasFlag(args.localizationCommentExtractionOptions, LocalizationCommentExtractionOptions.saveSourceInformation)),
            Cmd.flag("/tc", hasFlag(args.localizationCommentExtractionOptions, LocalizationCommentExtractionOptions.suppressComments)),
            Cmd.flag("/tm", hasFlag(args.localizationCommentExtractionOptions, LocalizationCommentExtractionOptions.suppressMnemonics))
        ] : []),
        Cmd.flag("/r", args.emitResourceFile),
        Cmd.flag("/v", args.verbose),
        ...((args.preprocessorSymbols || []).map(pair => pair.value === undefined
                                                            ? Cmd.argument("/d" + pair.name)
                                                            : Cmd.argument("/d" + pair.name + "=" + pair.value))),
        ...((args.undefinePreprocessorSymbols || []).map(s => Cmd.argument("/u" + s))),
        Cmd.option("/l ", args.defaultLanguageIdentifier),
        Cmd.option("/ln ", args.defaultLanguageName),
        Cmd.option("/I", Artifact.none(workingDirectory), !includes.isEmpty()),
        Cmd.options("/I", Artifact.inputs(includeSearchDirs)),
        Cmd.flag("/w", args.warnOnInvalidCodePage),
        Cmd.flag("/y", args.warnOnDuplicateControlIds),
        Cmd.flag("/n", args.generateNullTerminatedStrings),
        Cmd.flag("/x", args.ignoreIncludeEnvironmentVariable),
        Cmd.option("/q ", Artifact.input(args.rcConfigurationFile)),
        Cmd.option("/g ", args.ultimateFallbackLanguageIdentifier),
        Cmd.option("/gn ", args.ultimateFallbackLanguageName),
        Cmd.option("/g2 ", args.customFileVersionForChecksum),
        Cmd.option("/sl ", args.resourceStringLengthLimitPercentage > 0
                            && !(args.resourceStringLengthLimitPercentage > 100) ? args.resourceStringLengthLimitPercentage : undefined),
        Cmd.option("/z ", args.substituteFontName),
        Cmd.option("/c ", args.characterConversionCodePage !== 0 ? args.characterConversionCodePage : undefined),
        Cmd.option("/fo", Artifact.output(outputResFile)),
        ...(args.emitLanguageDependentResourceFile ? [
            Cmd.argument("/g1"),
            Cmd.option("/fm ", Artifact.output(outputMresFile))
        ]: []),
        args.emitLocalizationCommentExtractionFile
            ? Cmd.option("/tn ", Artifact.output(outputLceFile))
            : undefined,
        Cmd.argument(Artifact.input(source))
    ];

    let outputs = Transformer.execute({
        tool: args.tool || defaultTool(),
        arguments: cmdArgs,
        workingDirectory: workingDirectory,
        dependencies: [
            ...(args.resources || []),
            ...includes
        ],
        tags: args.tags,
    });

    return <RcOutput>{
        resFile: outputs.getOutputFile(outputResFile),
        multilingualFile: outputMresFile ? outputs.getOutputFile(outputMresFile) : undefined,
        lceFile: outputLceFile ? outputs.getOutputFile(outputLceFile) : undefined,
    };
}

// TODO: Implement & operator and possibly inline this function.
function hasFlag(v: LocalizationCommentExtractionOptions, o: LocalizationCommentExtractionOptions) : boolean {
    // Interpreter has "hasFlag" method that is not exposed by the IDE. But this method will be deprecated soon.
    return (v & o) === o;
}
