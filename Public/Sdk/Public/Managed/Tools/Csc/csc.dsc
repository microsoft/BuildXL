// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";
import * as Shared from "Sdk.Managed.Shared";

const pkgContents = Context.getCurrentHost().os === "win"
    ? importFrom("Microsoft.Net.Compilers").Contents.all
    : importFrom("Microsoft.NETCore.Compilers").Contents.all;

const cscTool = Context.getCurrentHost().os === "win"
    ? r`tools/csc.exe`
    : r`tools/bincore/csc.dll`;

export const tool: Transformer.ToolDefinition = Shared.Factory.createTool({
    exe: pkgContents.getFile(cscTool),
    runtimeDependencies: pkgContents.contents,
});

@@public
export const defaultArgs: Arguments = {
    out: undefined,
    sources: undefined,
    references: undefined,
    noLogo: true,
    noConfig: true,
    noStdLib: true,
    highEntropyVa: true,
    emitReferenceAssembly: true,
    emitDebugInformation: true,
    defines: undefined,
    utf8output: true,
    fullpaths: true,
    platform: "anycpu",
    checksumAlgorithm: "sha256",
    languageVersion: "Latest",
    deterministic: true,
    pathMap: [
            // Add a pathMap entry so that the PDB files and any generated paths that start
            // with those paths are replaced by the C# compiler. This helps by not leaking paths
            // where build happen. so if you have a repo with sourceroot d:\src\MyProject and a file
            // d:\src\MyProject\Some\Other\File.cs. The C# compiler will emit \.\Some\Other\File.cs as the
            // reported location.
            { key: d`${Context.getMount("SourceRoot").path}`, value: "\\." },
    ],
};

/**
 * Evaluate (i.e. schedule) C# compiler invocation using specified arguments.
 */
@@Tool.runner("csc.exe")
@@public
export function compile(inputArgs: Arguments) : Result {
    let args = defaultArgs.merge<Arguments>(inputArgs);

    const outputDirectory = Context.getNewOutputDirectory(args.out + "-csc");
    const outputBinPath = outputDirectory.combine(args.out);
    const outputPdbPath = (args.debugType || args.emitDebugInformation)
        ? (args.pdb ? p`${outputDirectory}/${args.pdb}` : outputBinPath.changeExtension(".pdb"))
        : undefined;
    const outputDocPath = args.doc && p`${outputDirectory}/${args.doc}`;
    const outputRefPath = args.emitReferenceAssembly ? p`${outputDirectory}/ref/${args.out}` : undefined;

    let cscArguments: Argument[] = [
        Cmd.flag("/nologo",         args.noLogo),
        Cmd.flag("/noconfig",       args.noConfig),

        Cmd.startUsingResponseFile(),

        Cmd.option("/out:",         Artifact.output(outputBinPath)),
        Cmd.option("/pdb:",         Artifact.output(outputPdbPath)),
        Cmd.option("/doc:",         Artifact.output(outputDocPath)),
        Cmd.option("/target:",      args.targetType ? args.targetType.toString() : undefined),
        Cmd.option("/platform:",    args.platform ? args.platform.toString() : undefined),
        Cmd.option("/refout:",      Artifact.output(outputRefPath)),

        Cmd.option("/langversion:", args.languageVersion),

        // TODO: uncoment the following line and delete the line after it once a new LKG is published
        // Cmd.option("/define:",       args.defines ? args.defines.join(";") : undefined),
        ...addIf((args.defines || []).length > 0, Cmd.rawArgument(`/define:"${args.defines.join(';')}"`)),

        Cmd.option("/nowarn:",       args.noWarnings ? args.noWarnings.map(n => n.toString()).join(",") : undefined),
        Cmd.flag("/warnaserror",     args.treatWarningsAsErrors),
        Cmd.option("/warnaserror-:", (args.treatWarningsAsErrors && args.warningsNotAsErrors) ? args.warningsNotAsErrors.join(",") : undefined),
        Cmd.option("/warnaserror:",  (args.treatWarningsAsErrors && args.warningsAsErrors)    ? args.warningsAsErrors.join(",")    : undefined),
        Cmd.option("/warn:",         toWarningLevelNumber(args.warningLevel)),

        Cmd.sign("/unsafe",                args.allowUnsafeBlocks),
        Cmd.sign("/checked",               args.checked),
        Cmd.sign("/optimize",              args.optimize),
        Cmd.sign("/delaysign",             args.delaySign),
        Cmd.sign("/nostdlib",              args.noStdLib),
        Cmd.sign("/debug",                 args.emitDebugInformation),
        Cmd.sign("/highentropyva",         args.highEntropyVa),
        Cmd.option("/subsystemversion:",   args.subSystemVersion),

        Cmd.option("/checksumalgorithm:",  args.checksumAlgorithm ? args.checksumAlgorithm.toString() : undefined),

        Cmd.option("/debug:",              args.debugType ? args.debugType.toString() : undefined),
        Cmd.flag("/embed",                 args.embed),
        Cmd.options("/embed:",             Artifact.inputs(args.embedFiles)),
        Cmd.option("/main:",               args.main),
        Cmd.option("/filealign:",          args.filealign),
        Cmd.flag("/fullpaths",             args.fullpaths),
        Cmd.option("/baseaddress:",        args.baseAddress),

        Cmd.flag("/errorendlocation",      args.errorEndLocation),
        Cmd.option("/preferreduilang:",    args.preferredUiLang),
        Cmd.option("/moduleassemblyname:", args.moduleAssemblyName),
        Cmd.option("/test:",               args.moduleName ? ("moduleName=" + args.moduleName) : undefined),
        Cmd.option("/errorreport:",        args.errorReport ? args.errorReport.toString() : undefined),
        Cmd.flag("/deterministic",         args.deterministic),
        ...(args.pathMap || []).map(entry =>
            Cmd.option("/pathMap:", Cmd.join("", [Artifact.none(entry.key), "=", entry.value]))),

        Cmd.option("/keyfile:",         Artifact.input(args.keyFile)),
        Cmd.option("/win32res:",        Artifact.input(args.win32Resources)),
        Cmd.option("/win32icon:",       Artifact.input(args.win32Icon)),
        Cmd.option("/win32manifest:",   Artifact.input(args.win32Manifest)),
        Cmd.option("/ruleset:",         Artifact.input(args.codeAnalysisRuleset)),

        Cmd.flag("/nowin32manifest",    args.nowin32Manifest),

        Cmd.option("/appconfig:",       Artifact.input(args.appConfig)),
        Cmd.options("/addmodule:",      Artifact.inputs(args.modules)),
        Cmd.options("/link:",           Artifact.inputs(args.link)),
        Cmd.options("/r:",              Artifact.inputs(args.references && args.references.map(r => r.binary))),

        ...(args.aliasedReferences || []).map(r =>
            Cmd.option(`/r:${r.alias}=`, Artifact.input(r.assembly.binary))),

        Cmd.options("/lib:",            Artifact.inputs(args.lib)),
        Cmd.options("/analyzer:",       Artifact.inputs(args.analyzers && args.analyzers.map(a => a.binary))),
        Cmd.options("/additionalfile:", Artifact.inputs(args.additionalFiles || [])),
        Cmd.option("/features:",        args.features ? Cmd.join(",", args.features) : undefined    ),

        // If this looks very complicated, user can use imperative for loops to build this piece manually
        Cmd.options("/resource:",       Artifact.inputs(args.resourceFiles)),
        ...(args.linkResources || []).map(lr =>
            Cmd.option("/resource:", Cmd.join(",", [Artifact.input(lr.file), lr.logicalName, lr.isPublic ? "public" : "private"]))),

        ...(args.sourceFolders || []).map(sourceFolder =>
            Cmd.option("/recurse:",  Cmd.join("", [Artifact.input(sourceFolder), "\\*"]))
        ),

        Cmd.files(args.sources),
    ];

    let additionalDependencies = [
        ...(args.implicitSources || [])
    ];

    let cscExecuteArgs : Transformer.ExecuteArguments = {
        tool: args.tool || tool,
        arguments: cscArguments,
        workingDirectory: outputDirectory,
        dependencies: additionalDependencies.filter(f => f !== undefined), //TODO: or additionalInputs???
        tags: ["compile"],
    };

    if (Context.getCurrentHost().os !== "win") {
        cscExecuteArgs = importFrom("Sdk.Managed.Frameworks").Helpers.wrapInDotNetExeForCurrentOs(cscExecuteArgs);
    }
    let executeResult = Transformer.execute(cscExecuteArgs);

    // Compose result object
    const binary = Shared.Factory.createBinaryFromFiles(
        executeResult.getOutputFile(outputBinPath),
        outputPdbPath && executeResult.getOutputFile(outputPdbPath),
        outputDocPath && executeResult.getOutputFile(outputDocPath)
    );

    const referenceBinary = outputRefPath
        ? Shared.Factory.createBinaryFromFiles(executeResult.getOutputFile(outputRefPath))
        : undefined;

    return {
        binary: binary,
        reference: referenceBinary,
    };
}

/**
 * User-centric set of arguments for running C# compiler.
 */
@@public
export interface Arguments extends Transformer.RunnerArguments{
    // core options

    /** Specifies the name of the assembly to generate. If omitted, the name of the output file is based on the name of the initial input file (in the case of *.dll assemblies) or the class defining the Main() method (in the case of *.exe assemblies). */
    out?: string;
    /** Specify debug information file name (default: output file name with .pdb extension )*/
    pdb?: string;
    /** When passed a reference assembly will be generated */
    emitReferenceAssembly?: boolean;

    /** C# source files to compile */
    sources?: File[];
    /** Informs csc.exe to compile C# files located in a project's subdirectory structure. This flag supports wildcard syntax. */
    sourceFolders?: StaticDirectory[];
    /** Used to specify the external assemblies to reference in the current compilation. */
    references?: Shared.Binary[];

    // output
    /** Specifies the file format of the assembly to be created. E.g. 'library', 'exe' etc. */
    targetType?: TargetType;
    /** Used to generate XML documentation files. */
    doc?: string;
    /** Specifies the CPU that must be present to host the assembly (x86, Itanium, x64, anycpu). The default is anycpu. */
    platform?: Platform;

    // input files

    /** Custom syntax version of references */
    aliasedReferences?: AliasedReference[];
    /** Used to specify the modules to include in a multifile assembly. */
    modules?: File[];
    /** Embed metadata from the specified interop assembly files (Short form: /l) */
    link?: File[];

    // resource
    /** Specify a Win32 resource file (.res) */
    win32Resources?: File;
    /** Use this icon for the output */
    win32Icon?: File;
    /** Specify a Win32 manifest file (.xml) */
    win32Manifest?: File;
    /** Do not include the default Win32 manifest */
    nowin32Manifest?: boolean;
    /** Embed the specified resource (Short form: /res) */
    resourceFiles?: File[];
    /** Link the specified resource to this assembly (Short form: /linkres) Where the resinfo format is <file>[,<string name>[,public|private]] */
    linkResources?: Shared.LinkResource[];

    // code generation
    /** Emit debugging information */
    emitDebugInformation?: boolean;
    /** Specify debugging type ('full' is default, 'portable' is a cross-platform format, 'embedded' is a cross-platform format embedded into the target .dll or .exe) */
    debugType?: DebugType;
    /** Embed all source files in the PDB. */
    embed?: boolean;
    /** Embed specific files in the PDB */
    embedFiles?: File[];
    /** When enabled (/optimize+) you instruct the compiler to generate the smallest and fastest assembly as possible. This option emits metadata that also instructs the CLR to optimize code at run time. */
    optimize?: boolean;

    // errors and warnings
    /** Report all warnings as errors */
    treatWarningsAsErrors?: boolean;
    /** Report specific warnings not as errors */
    warningsNotAsErrors?: number[];
    /** Report specific warnings as errors */
    warningsAsErrors?: number[];
    /** Set warning level (0-4) (Short form: /w) */
    warningLevel?: WarningLevel;
    /** Disable specific warning messages. E.g. [28] to disable warning CS0028*/
    noWarnings?: number[];

    // language
    /** Specifies if integer arithmetic that overflows the bounds of the data type will cause an exception at run time. */
    checked?: boolean;
    /** When enabled, this option allows your C# files to declare an unsafe scope, which are typically used to manipulate C++ style pointers. */
    allowUnsafeBlocks?: boolean;
    /** Define conditional compilation symbol(s) (Short form: /d) */
    defines?: string[];
    /** This option instructs the compiler to only use ISO-1 C# language features, i.e which basically boils down to C# 1.0 language features. */
    languageVersion?: string;

    // security
    /** Allows you to build an assembly using delayed signing of the strong name. */
    delaySign?: boolean;
    /** Specifies the path to the *.snk file used to strongly name the assembly. */
    keyFile?: File;
    /** Specifies the name of the container containing the *.snk files.  */
    keyContainer?: string;
    /** Enable high-entropy ASLR */
    highEntropyVa?: boolean;
    /** Specify algorithm for calculating source file checksum stored in PDB. Supported values are: SHA1 (default) or SHA256. */
    checksumAlgorithm?: ChecksumAlgorithm;

    // miscellaneous
    /** Suppress compiler copyright message */
    noLogo?: boolean;
    /** Do not auto include CSC.RSP file */
    noConfig?: boolean;

    // advanced
    /** This option allows you to specify the desired base address to load a *.dll. By default, the base address is chosen by the CLR. */
    baseAddress?: string;
    /** Run the analyzers from this assembly (Short form: /a) */
    analyzers?: Shared.Binary[];

    /** A set of strings designating experimental compiler features that are to be enabled. */
    features?: string[];

    /** Names additional files that don't directly affect code generation but may be used by analyzers for producing errors or warnings. */
    additionalFiles?: File[];

    // bugReport?: Path; // Output is not part of Assembly or consumed
    /** Specifies the code page to use for all source code files in the compilation. */
    codepage?: number;
    /** This option informs the compiler to output data using UTF-8 encoding. */
    utf8output?: boolean;
    /** If your current project defines multiple Main() methods, which can be helpful during unit testing, this flag allows you to specify which Main() method to execute when the assembly loads. */
    main?: string;
    /** Compiler generates fully qualified paths */
    fullpaths?: boolean;
    /** This option controls the section sizing within the output assembly (512, 1024, 2048, 4096 or 8192 bytes). If you are targeting a handheld device, such as a Pocket PC, /filealign can be used to specify the smallest possible sections. */
    filealign?: number;
    /** Output line and column of the end location of each error */
    errorEndLocation?: boolean;
    /** Specify the preferred output language name. */
    preferredUiLang?: string;
    /** By default, assembly manifests automatically reference mscorlib.dll. Specifying this option disables this behavior. */
    noStdLib?: boolean;
    /** Specify subsystem version of this assembly */
    subSystemVersion?: string;
    /** Specify additional directories to search in for references */
    lib?: File[];
    /** Specify how to handle internal compiler errors: 'prompt', 'send', 'queue', or 'none'. The default is queue. */
    errorReport?: ErrorReportStyle;
    /**Specify an application configuration file containing assembly binding settings */
    appConfig?: File;
    /** Name of the assembly which this module will be a part of */
    moduleAssemblyName?: string;
    /** Specify the name of the source module */
    moduleName?: string;

    /** A set of source files the compiler will read in certain cases but are not additionalFiles, nor on the commandline as source files. */
    implicitSources?: File[];
    /** Specify a ruleset file that disables specific diagnostics. */
    codeAnalysisRuleset?: File;
    /** Produce a deterministic assembly (including module version GUID and timestamp) */
    deterministic?: boolean;
    /** Specify a mapping for source path names output by the compiler to substitute the paths in the output PDBs*/
    pathMap?: PathMapEntry[];
}

@@public
export interface Result {
    binary: Shared.Binary,
    reference?: Shared.Binary,
}

@@public
export type TargetType = "exe" | "winexe" | "library" | "module" | "appcontainerexe" | "winmdobj";

@@public
export type ErrorReportStyle = "none" | "prompt" | "queue" | "send";

@@public
export type ChecksumAlgorithm = "sha1" | "sha256";

@@public
export type Platform = "x86" | "x64" | "itanium" | "arm" | "anycpu32bitpreferred" | "anycpu";

@@public
export type DebugType = "full" | "pdbOnly" | "portable";

@@public
export type WarningLevel = "level 0" | "level 1" | "level 2" | "level 3" | "level 4";

@@public
export interface AliasedReference {
    alias: string;
    assembly: Shared.Binary;
}

@@public
export interface PathMapEntry {
    key: Directory;
    value: string;
}

function toWarningLevelNumber(warningLevel: WarningLevel): number {
    if (warningLevel === undefined)  return undefined;

    switch (warningLevel) {
        case "level 0": return 0;
        case "level 1": return 1;
        case "level 2": return 2;
        case "level 3": return 3;
        case "level 4": return 4;
        default:                  return Contract.fail("Unexpected WarningLevel '" + warningLevel + "'.");
    }
}
