// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

import {Shared} from "Sdk.Native.Shared";

// TODO: Some previously imported files are inlined in this file because the type checker does not support namespace merging.
/**
 * Transformer for Cnc.exe (C-Natural compiler)
 *
 * Syntax:
 *    /Out:"string"                       short form /O
 *    /Target:{Source|Exe|WinExe|Library|Module}    short form /T
 *    /Reference:"string"                 short form /R
 *    /Debug[+|-]
 *    /Checked[+|-]                       short form /C
 *    /Unsafe[+|-]                        short form /U
 *    /Define:"string"                    short form /D
 *    /NoLogo[+|-]                        short form /N
 *    /NoStdLib[+|-]
 *    /NoWordWrap[+|-]
 *    /Platform:{amd64|armv4i|armv6|armv7|arm|x86|ppc|arm64}    short form /P
 *    /System:{Windows|XBox|CE5|CE7|Phone8|MinWin}    short form /S
 *    /BaseAddress:"string"               short form /B
 *    /FullPaths[+|-]                     short form /F
 *    /Verbosity:"int"                    short form /V
 *    /CnCopyright:"string"
 *    /CnExportPrefix:"string"
 *    /CnMergeExportDefs[+|-]
 *    /CnFakeGenerics[+|-]
 *    /CnHost:{None|Custom}
 *    /CnSystemTypeIsStruct
 *    /CnPlugin:"string"
 *    /CnReflection:{None|Explicit|Public}
 *    /CnRuntimeLib:"string"
 *    /CnThreading:{Single|Multiple|Compatible}
 *    /Environment:"string"               short form /Env
 *    /CppFPO[+|-]
 *    /CppPCH:"string"
 *    /CppOut:"string"
 *    /CppShowIncludes[+|-]
 *    /CppIncludeDirs:"string"
 *    /CppLibDirs:"string"
 *    /Optimize[+|-]
 *    /CppCompiler:"string"               short form /CL
 *    /CppLinker:"string"                 short form /LINK
 *    /ResearchNoCheckBounds[+|-]
 *    /ResearchNoTypeCastChecks[+|-]
 *    /ReportStringUsage[+|-]
 *    @"file"                             Read response file for more options
 *    "FileNames"
 */

/**
* Host type {None|Custom}
*/
@@public
export const enum HostType {
    /**
     * Host type: none (default)
     */
    @@Tool.option("/CnHost:None")
    none,

    /**
     * Host type: Custom
     */
    @@Tool.option("/CnHost:Custom")
    custom
}

/**
 * Target type {Source|Exe|WinExe|Library|Module}
 */
@@public
export const enum OutputAssemblyType {
    /**
     * Target type: Source
     */
    @@Tool.option("/Target:source")
    source,

    /**
     * Target type: Exe
     */
    @@Tool.option("/Target:exe")
    exe,

    /**
     * Target type: WinExe
     */
    @@Tool.option("/Target:winexe")
    winExe,

    /**
     * Target type: Library
     */
    @@Tool.option("/Target:library")
    library,

    /**
     * Target type: Module
     * 'module' is a reserved keyword in DScript, using 'moduleOption'
     */
    @@Tool.option("/Target:module")
    moduleOption
}

/**
 * Provides dynamically loaded libraries during build
 * Interprets as /CnPlugin:CoreUIPlugins!MessagablePlugin
 */
@@public
export interface Plugin {
    /**
     * Assembly file to load.  Will be reduced to just the assembly name
     */
    assemblyFile: File;

    /**
     * Name space and class to create an instance of
     */
    pluginClassName: string;
}

/**
 * Reflection mode: {None|Explicit|Public}
 *
 * The ReflectionMode represents the level of reflection support
 * that the compiler has been requested to generate
 */
@@public
export const enum ReflectionModeType {
    /**
     * Reflection mode: none (default)
     */
    @@Tool.option("/CnReflection:None")
    none,

    /**
     * Reflection mode: Explicit
     */
    @@Tool.option("/CnReflection:Explicit")
    explicit,

    /**
     * Reflection mode: Public
     */
    @@Tool.option("/CnReflection:Public")
    public
}

/**
 * Platform {amd64|armv4i|armv6|armv7|arm|x86|ppc|arm64}
 */
@@public
export const enum TargetPlatformType {
    /**
     * Platform: AMD64
     */
    @@Tool.option("/Platform:amd64")
    x64,

    /**
     * Platform: ARMV4i
     */
    @@Tool.option("/Platform:armv4i")
    armV4I,

    /**
     * Platform: ARMV6
     */
    @@Tool.option("/Platform:armv6")
    armV6,

    /**
     * Platform: ARMV7
     */
    @@Tool.option("/Platform:armv7")
    armV7,

    /**
     * Platform: ARM
     */
    @@Tool.option("/Platform:arm")
    arm32,

    /**
     * Platform: X86
     */
    @@Tool.option("/Platform:x86")
    x86,

    /**
     * Platform: PPC
     */
    @@Tool.option("/Platform:ppc")
    ppc,

    /**
     * Platform: ARM64
     */
    @@Tool.option("/Platform:arm64")
    arm64
}

/**
* Target system
*/
@@public
export const enum TargetSystemType {
    /**
     * Target system: Windows
     */
    @@Tool.option("/System:Windows")
    windows,

    /**
     * Target system: XBox
     */
    @@Tool.option("/System:XBox")
    xbox,

    /**
     * Target system: CE5
     */
    @@Tool.option("/System:CE5")
    ce5,

    /**
     * Target system: CE7
     */
    @@Tool.option("/System:CE7")
    ce7,

    /**
     * Target system: Phone8
     */
    @@Tool.option("/System:Phone8")
    windowsPhone8,

    /**
     * Target system: MinWin
     */
    @@Tool.option("/System:MinWin")
    minWin
}

/**
 * Threading mode: {Single|Multiple|Compatible}
 * The multi-threading mode that a module is compiled with
 */
@@public
export const enum ThreadingModeType {
    /**
     * Threading mode: Single
     */
    @@Tool.option("/CnThreading:Single")
    single,

    /**
     * Threading mode: Multiple
     */
    @@Tool.option("/CnThreading:Multiple")
    multiple,

    /**
     * Threading mode: Compatible
     */
    @@Tool.option("/CnThreading:Compatible")
    compatible
}

/**
 * Arguments passed to cnc.exe
 *
 * See /mincore/Cn/CnCompiler/main.cs for argument options and defaults
 */
// @@toolName("cnc.exe")
@@public
export interface Arguments extends Transformer.RunnerArguments {
    /**
     * The input files (usually .cs)
     */
    sources: File[];

    /**
     * Name of the output file
     */
    @@Tool.option("/Out:")
    output: PathAtom;

    /**
     * Target type (Source, EXE, WinEXE, Library, Module)
     */
    outputType?: OutputAssemblyType;

    /**
     * Assembly references for importing
     */
    @@Tool.option("/Reference:")
    references?: File[];

    /**
     * Generate code that contains debugging info
     */
    @@Tool.option("/Debug[+|-]")
    generateDebuggableCode?: boolean;

    /**
     * Checked / unchecked blocks
     */
    @@Tool.option("/Checked[+|-]")
    checkedBlocks?: boolean;

    /**
     * Enable unsafe code generation
     */
    @@Tool.option("/Unsafe[+|-]")
    unsafeCodeGeneration?: boolean;

    /**
     *  Defines as name/value pairs
     */
    @@Tool.option("/Define:")
    defines?: Shared.StringPair[];

    /**
     * Use CnRuntime lib
     * Do not include CnRuntime.  This should only be turned "on" when compiling CnRuntime
     */
    @@Tool.option("/NoStdLib[+|-]")
    useStandardLibrary?: boolean;

    /**
     * Use/Prevent word-wrapping for error reporting
     */
    @@Tool.option("/NoWordWrap[+|-]")
    wordWrap?: boolean;

    /**
     * Target processor platform (x86, x64, ArmV4I, ArmV6, ArmV7, Arm32, Ppc)
     */
    targetPlatform?: TargetPlatformType;

    /**
     * Target operating system {Windows, XBox, CE5, CE7, Phone8, MinWin}
     */
    targetOS?: TargetSystemType;

    /**
     * Specifies the base address of the generated binary.
     * Accepts any representation of signed and unsigned integers of 32 and 64 bit sizes.  May be
     * set as ####:#### (segment:index), 0x8000C000, or 2147532800
     */
    @@Tool.option("/BaseAddress:")
    binaryBaseAddress?: string;

    /**
     * Full paths in errors/warnings
     * Compiler generates fully qualified paths for error / warning information
     */
    @@Tool.option("/FullPaths[+|-]")
    compilerFullPaths?: boolean;

    /**
     * Verbosity level
     * There's no known level values, CNC itself hard codes values as integers within its code
     * Verbosity undefined and verbosity 0 have the same meaning: verbosity is not passed to the tool command line
     */
    @@Tool.option("/Verbosity:")
    verbosity?: number;

    /**
     * Copyright string to be stored in output
     */
    @@Tool.option("/CnCopyright:")
    copyright?: string;

    /**
     * Prefix for exported functions
     */
    @@Tool.option("/CnExportPrefix:")
    exportPrefix?: string;

    /**
     * Suppress automatic appends to .DEF file - do a merge instead
     */
    @@Tool.option("/CnMergeExportDefs[+|-]")
    mergeExportDefinitions?: boolean;

    /**
     * Use fake generics mode
     */
    @@Tool.option("/CnFakeGenerics[+|-]")
    useFakeGenerics?: boolean;

    /**
     * Specifies an optional C++ hosting component to override the default behavior for various operations
     */
    @@Tool.option("/CnHost:")
    host?: HostType;

    /**
     * Make System.Type a struct type
     */
    @@Tool.option("/CnSystemTypeIsStruct")
    systemTypeIsStruct?: boolean;

    /**
     * Optional compiler plugin(s)
     */
    @@Tool.option("/CnPlugin:")
    plugins?: Plugin[];

    /**
     * Represents the level of reflection support that the compiler has been requested to generate
     */
    @@Tool.option("/CnReflection:")
    reflectionMode?: ReflectionModeType;

    /**
     * Location of generated "CnRuntime.lib" file
     */
    @@Tool.option("/CnRuntimeLib:")
    runtimeLibraryPath?: File;

    /**
     * Threading Mode returns the multi-threading mode that a module was compiled
     * with. This affects the "default" storage for 'static' fields.
     */
    @@Tool.option("/CnThreading:")
    threadingMode?: ThreadingModeType;

    /**
     * Optional set of "name=value" for settings that need to be passed to the compiler
     */
    @@Tool.option("/Env:")
    compilerOptions?: Shared.StringPair[];

    /**
     * Instruct the compiler to use FPO optimizations
     */
    @@Tool.option("/CppFPO[+|-]")
    cppUseFramePointerOmission?: boolean;

    /**
     * The precompiled header file (optional)
     */
    @@Tool.option("/CppPCH:")
    cppPrecompiledHeaderFile?: PathAtom;

    /**
     * Verbose output from C++ compiler about included files
     */
    @@Tool.option("/CppShowIncludes[+|-]")
    cppShowIncludes?: boolean;

    /**
     * Include directories to pass to C++ compiler
     */
    @@Tool.option("/CppIncludeDirs:")
    cppIncludeDirectories?: StaticDirectory[];

    /**
     * Library directories to pass to link.exe
     */
    @@Tool.option("/CppLibDirs:")
    linkLibraryDirectories?: StaticDirectory[];

    /**
     * Enable optimizations
     */
    @@Tool.option("/Optimize[+|-]")
    optimizations?: boolean;

    /**
     * Additional parameters to pass through to cl.exe
     */
    @@Tool.option("/CppCompiler:")
    cppCompilerParameters?: string[];

    /**
     * Additional parameters to pass through to LINK (cl.exe)
     */
    @@Tool.option("/CppLinker:")
    cppLinkerParameters?: string[];

    /**
     * Research: Turn off array index bounds checks
     */
    @@Tool.option("/ResearchNoCheckBounds[+|-]")
    arrayIndexBoundChecks?: boolean;

    /**
     * Research: Turn off type checks on casts
     */
    @@Tool.option("/ResearchNoTypeCastChecks[+|-]")
    typeChecksOnCasts?: boolean;

    /**
     * Generate string literal usage report
     */
    @@Tool.option("/ReportStringUsage[+|-]")
    reportStringUsage?: boolean;

    /**
     * Directory Path for CN Root.
     * This is required to set the Environment variable CN_ROOT which is
     * further used by the App.config
     */
    @@Tool.option("/CnRoot")
    cnRoot?: Path;
}

@@public
export function evaluate(inputArgs: Arguments): File {
    validateArguments(inputArgs);

    let outputDirectory = Context.getNewOutputDirectory("cnc");
    let outputFile = outputDirectory.combine(inputArgs.output);

    // Cnc tool will generate Module's .cn in out directory. This is an implicit output
    let cnOutput = outputFile.changeExtension(".cn");

    let tempDir = Context.getTempDirectory("cnc");

    let cmdArgs: Argument[] = [
        Cmd.option("/Out:", Artifact.output(outputFile)),
        Cmd.option("/CppOut:", Artifact.none(tempDir)), // Moving the interemediate generated files to temp as they are not deterministic and not needed in other pips.
        Cmd.startUsingResponseFile(false),
        Cmd.option("/Target:", outputAssemblyToString(inputArgs.outputType)),
        Cmd.options("/Reference:", inputArgs.references && Artifact.inputs(inputArgs.references)),
        Cmd.sign("/Debug", inputArgs.generateDebuggableCode),
        Cmd.sign("/Checked", inputArgs.checkedBlocks),
        Cmd.sign("/Unsafe", inputArgs.unsafeCodeGeneration),
        Cmd.options("/Define:", inputArgs.defines && inputArgs.defines.map(define => buildNameValueEquality(define))),
        Cmd.sign("/NoStdLib", inputArgs.useStandardLibrary),
        Cmd.sign("/NoWordWrap", !inputArgs.wordWrap),
        Cmd.option("/Platform:", targetPlatformTypeToString(inputArgs.targetPlatform)),
        Cmd.option("/System:", targetSystemTypeToString(inputArgs.targetOS)),
        Cmd.option("/BaseAddress:", String.isUndefinedOrEmpty(inputArgs.binaryBaseAddress)? undefined : inputArgs.binaryBaseAddress),
        Cmd.sign("/FullPaths", inputArgs.compilerFullPaths),
        Cmd.option("/Verbosity:", inputArgs.verbosity),
        Cmd.option("/CnCopyright:", String.isUndefinedOrEmpty(inputArgs.copyright)? undefined: inputArgs.copyright),
        Cmd.option("/CnExportPrefix:", String.isUndefinedOrEmpty(inputArgs.exportPrefix)? undefined: inputArgs.exportPrefix),
        Cmd.sign("/CnMergeExportDefs", inputArgs.mergeExportDefinitions),
        Cmd.sign("/CnFakeGenerics", inputArgs.useFakeGenerics),
        Cmd.option("/CnHost:", hostTypeToString(inputArgs.host)),
        Cmd.flag("/CnSystemTypeIsStruct", inputArgs.systemTypeIsStruct),
        //See \wm\src\uxplat\Cn\CnCompiler\Plugins\PluginManager.cs, LoadPlugin(...) for implementation
        Cmd.options("/CnPlugin:", inputArgs.plugins && inputArgs.plugins.all(validatePlugin) && inputArgs.plugins.map(
            plugin => plugin.assemblyFile.nameWithoutExtension.toString + "!" + plugin.pluginClassName)),
        Cmd.option("/CnReflection:", reflectionModeTypeToString(inputArgs.reflectionMode)),
        Cmd.option("/CnRuntimeLib:", Artifact.none(inputArgs.runtimeLibraryPath && inputArgs.runtimeLibraryPath.parent)),
        Cmd.option("/CnThreading:", threadingModeTypeToString(inputArgs.threadingMode)),
        Cmd.options("/Env:", inputArgs.compilerOptions &&
            inputArgs.compilerOptions.map(compilerOption => buildNameValueEquality(compilerOption))),
        Cmd.sign("/CppFPO", inputArgs.cppUseFramePointerOmission),
        Cmd.option("/CppPCH:", inputArgs.cppPrecompiledHeaderFile),
        Cmd.sign("/CppShowIncludes", inputArgs.cppShowIncludes),
        Cmd.option("/CppIncludeDirs:", inputArgs.cppIncludeDirectories && Cmd.join(";", Artifact.inputs(inputArgs.cppIncludeDirectories))),
        Cmd.option("/CppLibDirs:", inputArgs.linkLibraryDirectories && Cmd.join(";", Artifact.inputs(inputArgs.linkLibraryDirectories))),
        Cmd.sign("/Optimize", inputArgs.optimizations),
        Cmd.options("/CL:", inputArgs.cppCompilerParameters),
        Cmd.options("/LINK:", inputArgs.cppLinkerParameters),
        Cmd.sign("/ResearchNoCheckBounds", inputArgs.arrayIndexBoundChecks),
        Cmd.sign("/ResearchNoTypeCastChecks", inputArgs.typeChecksOnCasts),
        Cmd.sign("/ReportStringUsage", inputArgs.reportStringUsage),
        Cmd.files(inputArgs.sources)
    ];

    // Setting up environment variables
    // - Set the environment Variable for CN_ROOT path if directory is given
    // - Add cl.exe and Link.exe information in Environment variable PATH as CnC will eventually refer it

    // Computes all distinct paths from tool.exe and runtime dependencies, recursively, including nested tools
    let paths = [
        ...inputArgs.tool.nestedTools.mapMany(tool => getExeAndRuntimeDependencyPaths(tool)),
        ...getExeAndRuntimeDependencyPaths(inputArgs.tool)
    ].unique();

    let environmentVars = [
        ...(inputArgs.cnRoot? [{name: "CN_ROOT", value:inputArgs.cnRoot}] : []),
        {name: "PATH", value:paths, separator: ";"}
    ];

    let result = Transformer.execute({
        tool: inputArgs.tool,
        workingDirectory: Context.getSpecFileDirectory(),
        arguments: cmdArgs,
        dependencies: [
            ...(inputArgs.plugins? inputArgs.plugins.map(plugin => plugin.assemblyFile) : []),
            ...(inputArgs.runtimeLibraryPath? [inputArgs.runtimeLibraryPath] : [])
        ],
        tempDirectory: tempDir,
        environmentVariables: environmentVars,
        implicitOutputs: [
            cnOutput
        ]
    });

    return result.getOutputFile(outputFile);
}

type StringOrPair = Shared.StringPair | string;

/**
 * Given a StringPair, builds the string "name=value" if value is not undefined or whitespace. Otherwise, builds the string "name".
 */
function buildNameValueEquality(nameValue: StringOrPair): string {
    if (typeof nameValue === "string") {
        return nameValue;
    } else {
        return nameValue.name + (String.isUndefinedOrEmpty(nameValue.value) ? "" : "=" + nameValue.value);
    }
}

function getExeAndRuntimeDependencyPaths(tool: Transformer.ToolDefinition): Path[] {
    return [
        ...(tool.runtimeDependencies? tool.runtimeDependencies.map(runtimeDependency => runtimeDependency.parent.path): []),
        tool.exe.path
    ];
}

/**
 * Validates required arguments.
 * There should be at least one source file and the output folder has to be specified.
 */
function validateArguments(args: Arguments) : boolean {
    if (args.tool === undefined) {
        Contract.fail("Tool must be specified.");
        return false;
    }

    if (!args.sources || args.sources.length === 0) {
        Contract.fail("Sources must not be empty.");
        return false;
    }

    if(!args.output) {
        Contract.fail("Output must be specified.");
        return false;
    }

    return true;
}

function hostTypeToString(hostType: HostType): string {
    switch(hostType) {
        case undefined:
            return undefined;
        case HostType.none:
            return "None";
        case HostType.custom:
            return "Custom";
        default:
            Contract.fail("Unexpected HostType: '" + hostType.toString() + "'.");
            return undefined;
    }
}

function outputAssemblyToString(outputType: OutputAssemblyType): string {
    switch(outputType) {
        case undefined:
            return undefined;
        case OutputAssemblyType.source:
            return "Source";
        case OutputAssemblyType.exe:
            return "Exe";
        case OutputAssemblyType.winExe:
            return "WinExe";
        case OutputAssemblyType.library:
            return "Library";
        case OutputAssemblyType.moduleOption:
            return "Module";
        default:
            Contract.fail("Unexpected OutputAssemblyType: '" + outputType.toString() + "'.");
            return undefined;
    }
}

function validatePlugin(plugin: Plugin): boolean {
    if (plugin.assemblyFile === undefined) {
        Contract.fail("Field 'assemblyFile' should be defined.");
        return false;
    }

    if (String.isUndefinedOrWhitespace(plugin.pluginClassName)) {
        Contract.fail("Field 'pluginClassName' should be non-empty.");
        return false;
    }

    return true;
}

function reflectionModeTypeToString(reflectionMode: ReflectionModeType): string {
    switch(reflectionMode) {
        case undefined:
            return undefined;
        case ReflectionModeType.none:
            return "None";
        case ReflectionModeType.explicit:
            return "Explicit";
        case ReflectionModeType.public:
            return "Public";
        default:
            Contract.fail("Unexpected ReflectionModeType: '" + reflectionMode.toString() + "'.");
            return undefined;
    }
}

function targetPlatformTypeToString(targetPlatform: TargetPlatformType): string {
    switch(targetPlatform) {
        case undefined:
            return undefined;
        case TargetPlatformType.x64:
            return "amd64";
        case TargetPlatformType.armV4I:
            return "armv4i";
        case TargetPlatformType.armV6:
            return "armv6";
        case TargetPlatformType.armV7:
            return "armv7";
        case TargetPlatformType.arm32:
            return "arm";
        case TargetPlatformType.x86:
            return "x86";
        case TargetPlatformType.ppc:
            return "ppc";
        case TargetPlatformType.arm64:
            return "arm64";
        default:
            Contract.fail("Unexpected TargetPlatformType: '" + targetPlatform.toString() + "'.");
            return undefined;
    }
}

function targetSystemTypeToString(targetSystem: TargetSystemType): string {
    switch(targetSystem) {
        case undefined:
            return undefined;
        case TargetSystemType.windows:
            return "Windows";
        case TargetSystemType.xbox:
            return "XBox";
        case TargetSystemType.ce5:
            return "CE5";
        case TargetSystemType.ce7:
            return "CE7";
        case TargetSystemType.windowsPhone8:
            return "Phone8";
        case TargetSystemType.minWin:
            return "MinWin";
        default:
            Contract.fail("Unexpected TargetSystemType: '" + targetSystem.toString() + "'.");
            return undefined;
    }
}

function threadingModeTypeToString(threadingMode: ThreadingModeType): string {
    switch(threadingMode) {
        case undefined:
            return undefined;
        case ThreadingModeType.single:
            return "Single";
        case ThreadingModeType.multiple:
            return "Multiple";
        case ThreadingModeType.compatible:
            return "Compatible";
        default:
            Contract.fail("Unexpected ThreadingModeTypeToString: '" + threadingMode.toString() + "'.");
            return undefined;
    }
}
