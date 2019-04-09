// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

import {Shared, PlatformDependentQualifier} from "Sdk.Native.Shared";

export declare const qualifier: PlatformDependentQualifier;

/**
 * Settings for a template file argument
 */
@@public
export interface TemplateFileArgument {
    /**
     * The name of the file to be renamed
     */
    name: File;

    /**
     * The extension of the file, usually tmh
     */
    newExtension: PathAtom;
}

/**
 * WPP Processor tool options
 */
@@public
export interface WppOptions {
    /**
     * Configuration Directories
     * Specifies the location of configuration and template files. 
     * Paths represent the fully qualified path to a directory. 
     * You can specify multiple paths. The default is the local directory.
     * This is required because the value cannot be assumed or defaulted
     * by the runner.
     * -cfgdir:Path1[;Path2]
     */
    @@Tool.option("-cfgdir")
    configurationDirectories?: StaticDirectory[];

    /**
     * Specify Control GUID
     * Defines a WPP_CONTROL_GUIDS macro with the specified control GUID
     * and WPP_DEFINE_BIT entries named "Error," "Unusual,"
     * and "Noise." This is an alternative to adding the
     * macro to the source file. GUID represents the control GUID.
     * -ctl:GUID
     */
    @@Tool.option("-ctl")
    controlGuid?: string;

    /**
     * Enable DLL Macro
     * Defines the WPP_DLL macro. When this macro is defined, the WPP
     * data structures are initialized whenever WPP_INIT_TRACING is
     * called. Otherwise the structures are initialized only once.
     * -dll
     */
    @@Tool.option("-dll")
    enableDllMacro?: boolean;

    /**
     * Preprocessor Definitions
     * -DmacroName - Adds #define macroName to the beginning of the generated file
     * -DmacroName=value - Adds #define macroName Value to the beginning of the
     * generated file. This option has the same effect as the /D
     * (define a macro) compiler option. It is included to insure that
     * defines are valid at the start of the TMH files.
     * -DmacroName
     * -DmacroName=value
     */
    @@Tool.option("-D")
    preprocessorSymbols?: Shared.PreprocessorSymbol[];

    /**
     * Trace Kernel-Mode Components
     * Defines the WPP_KERNEL_MODE macro, which traces kernel-mode
     * components. By default, only user-mode components are traced.
     * -km
     */
    @@Tool.option("-km")
    traceKernelModeComponents?: boolean;

    /**
     * Specify Module Name
     * Defines the name that will be displayed when decoding events,
     * the default value displayed is the directory name. Used to
     * connect events that are part of a logical module but in
     * separate directories
     * -p:ModuleName
     */
    @@Tool.option("-p")
    moduleName?: string;

    /**
     * Numeric base for numbering of format strings
     * Establishes a numeric base for numbering of format strings,
     * such as "%1!d!, %2!s!." The default is 1.
     * -argbase:Number
     */
    @@Tool.option("-argbase")
    argBase?: number;

    /**
     * Function To Generate Trace Messages
     * Specifies alternatives to the DoTraceMessage function.
     * These functions can then be used to generate trace messages.
     * -func (DoMyTraceMessage(LEVEL,FLAGS,MSG,...)
     */
    @@Tool.option("-func")
    functionToGenerateTraceMessages?: string[];

    /**
     * Ignore Exclamation Marks
     * Directs WPP to ignore exclamation marks, also known as "shrieks" used in complex formatting, such as %!timestamp!%
     * By default, exclamation marks are required and WPP tries to interpret them.
     * -noshrieks
     */
    @@Tool.option("-noshrieks")
    ignoreExplanationMarks?: boolean;

    /**
     * Alternate Configuration File
     * Specifies an alternate configuration file. WPP uses this file instead of the defaultwpp.ini file
     * located in (%SYSTEMROOT%\WINDDK\Version\wppconfig\rev1\defaultwpp.ini)
     * -defwpp:path
     */
    @@Tool.option("-defwpp")
    alternateConfigFile?: File;

    /**  
     * File Extensions
     * Specifies the file types that WPP recognizes as source files
     * WPP ignores files with a different file name extension.
     * By default, WPP recognizes only .c, .c++, .cpp, and .cxx files.
     * This option allows you to use sources as an input to WPP
     * without having to delete or rename resource files that WPP
     * doesn't use, such as .rc and .mc files.
     * For example, to add tracing to C++ files and header (.h) files, use the following command:
     * -ext:.cpp.CPP.h.H 
     * Also, to give the TMH files for the C++ and header files different names, use the -preserveext option.
     * -ext:ext1[.ext2]
     */
    @@Tool.option("-ext")
    extensionsList?: PathAtom[];

    /**
     * Specify Template File
     * For every source file that WPP processes with the name
     * specified between braces {}, create another file with the
     * specified file name extension. File.tpl represents the source
     * file. *.ext represents the type of file that is created and
     * its file name extension. You can specify multiple -gen options
     * For example, -gen{um-default.tpl}*.tmh means that for every
     * um-default.tpl file that WPP processes, it produces a
     * um-default.tmh file. 
     * -gen{file.tpl}*.ext
     */
    @@Tool.option("-gen")
    templateFileList?: TemplateFileArgument[];

    /**
     * Additional Configuration File
     * Specifies an additional configuration file. WPP uses this file in addition to the 
     * default file, defaultwpp.ini.
     * -ini:path
     */
    @@Tool.option("-ini")
    additionalConfigFile?: File;

    /**
     * Preserve File Extensions
     * Preserves the specified file name extensions when creating TMH files. 
     * By default, TMH files for all file types are named filename.tmh. 
     * This caused file name conflicts when you have more than one source file with the same name. 
     * For example, by default, TMH files for C files (.c) and header (.h)files would be named filename.tmh. 
     * By using -preserveext:.c .h, the TMH files are named filename.c.tmh and filename.h.tmh.
     * -preserveext:.ext1[.ext2]
     */
    @@Tool.option("-preserveext")
    preserveExtensions?: PathAtom[];

    /**
     * Scan Configuration Data
     * Searches for configuration data, such as custom data types, in a file that is not a 
     * configuration file, as well as in defaultwpp.ini. If you added the configuration data 
     * to a custom configuration file, use the AdditionalConfigFile parameter.
     * -scan:file
     */
    @@Tool.option("-scan")
    filesToScan?: File[];

    /**
     * Suppress Unicode warning
     * WPP does not support Unicode source files, and will print an
     * error message, unless this switch is specified to suppress the
     * error. This switch should only be used if the Unicode file does
     * contain any Trace statements.
     * -unicodeIgnore
     */
    @@Tool.option("-unicodeIgnore")
    suppressWarningsOnUnicode?: boolean;
}

/**
 * WPP Processor tool
 * Documentation for commands is available at 
 * http://msdn.microsoft.com/en-us/library/windows/hardware/ff556201(v=vs.85).aspx
 */
@@public
export interface Arguments extends Transformer.RunnerArguments, WppOptions {
    /**
     * Specifies a list of source files
     */
    sources: File[];
}

/**
 * The output generated by WPP Trace
 */
@@public
export interface Result {
    /**
     * Header files containing WPP tracing macros.
     */
    traceOutput: StaticDirectory;
}

/**
 * File extensions that this tool doesn't silently ignore
 */
const allowedExtensions = Set.empty<PathAtom>().add(a`.c`).add(a`.cxx`).add(a`.cpp`).add(a`.c++`);

/** The Wpp tool definition */
export function defaultTool(): Transformer.ToolDefinition {
    return undefined;
}

/**
 * The default arguments for the Wpp runner
 */
export const defaultArgs = <Arguments>{
    sources: [],
    configurationDirectories: [],
    preprocessorSymbols: [],
    argBase: 1,
    functionToGenerateTraceMessages: [],
    extensionsList: [],
    templateFileList: [],
    preserveExtensions: [],
    filesToScan: [],
};

/**
 * Run WppTrace.exe
 */
@@Tool.runner("tracewpp.exe")
@@public
export function evaluate(args: Arguments): Result {
    args = defaultArgs.override<Arguments>(args);

    let outputDirectory = Context.getNewOutputDirectory("wpp");
    let dependencies: Transformer.InputArtifact[] = [];
    let implicitOutputs: Transformer.OutputArtifact[] = [];
    let cmdArgs: Argument[] = [];

    cmdArgs = cmdArgs.push(Cmd.argument("-q"));
    for (let source of args.sources) {
        cmdArgs = cmdArgs.push(Cmd.argument(Artifact.input(source)));
        if (allowedExtensions.contains(source.extension)) {
            implicitOutputs = implicitOutputs.push(Path.interpolate(outputDirectory, source.name).changeExtension(a`.tmh`));
        }
    }

    for (let templateFile of args.templateFileList) {
        cmdArgs = cmdArgs.push(Cmd.argument("-gen:{" + templateFile.name.toString() + "}*" + templateFile.newExtension.toString()));
        dependencies = dependencies.push(templateFile.name);
        implicitOutputs = implicitOutputs.push(Path.interpolate(outputDirectory, templateFile.name.name).changeExtension(templateFile.newExtension));
    }

    cmdArgs = cmdArgs.concat([
        Cmd.option("-odir:", Artifact.none(outputDirectory)),
        Cmd.option("-cfgdir:", Cmd.join(";", args.configurationDirectories.map(dir => Artifact.input(dir)))),
        Cmd.flag("-dll", args.enableDllMacro),
        Cmd.option("-ctl:", args.controlGuid),
        Cmd.options("-func:", args.functionToGenerateTraceMessages),
        Cmd.option("-p:", args.moduleName),
        Cmd.option("-ini:", Artifact.input(args.additionalConfigFile)),
        Cmd.option("-preserveext:", args.preserveExtensions.join(""), args.preserveExtensions.length > 0),
        Cmd.option("-ext:", args.extensionsList.join(""), args.extensionsList.length > 0),
        Cmd.options("-scan:", Artifact.inputs(args.filesToScan)),
        Cmd.option("-defwpp:", Artifact.input(args.alternateConfigFile)),
        Cmd.option("-argbase:", args.argBase, args.argBase !== 1),
        Cmd.flag("-km", args.traceKernelModeComponents),
        Cmd.flag("-noshrieks", args.ignoreExplanationMarks),
        Cmd.flag("-unicodeIgnore", args.suppressWarningsOnUnicode),
        Cmd.options("-D", (args.preprocessorSymbols || []).mapDefined(Shared.preprocessorSymbolToString)),
    ]);

    let transformerExecution = Transformer.execute({
        tool: args.tool || defaultTool(),
        workingDirectory: Context.getSpecFileDirectory(),
        arguments: cmdArgs,
        dependencies: dependencies,
        implicitOutputs: implicitOutputs
    });

    return <Result>{
        traceOutput: Transformer.sealDirectory({
            root: outputDirectory, 
            files: transformerExecution.getOutputFiles()
        })
    };
}
