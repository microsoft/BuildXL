// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

import {Shared, PlatformDependentQualifier} from "Sdk.Native.Shared";

import * as Cl   from "Sdk.Native.Tools.Cl";
import * as Link from "Sdk.Native.Tools.Link";

export declare const qualifier: PlatformDependentQualifier;

export const defaultLibArguments: Arguments = {
    suppressStartupBanner: true,
    libraryType: LibraryType.staticType,
    inputFormat: InputFormat.commonObjectFileFormat,
    sources: [],
    resources: [],
    libraries: [],
    platform: Shared.Platform.x86,
    ignoreAllDefaultLibraries: false,
    defaultLibrariesToIgnore: [],
    includeSymbols: [],
    exports: []
};

/**
 * Controls which input formats are supported.
 */
@@public
export const enum InputFormat { commonObjectFileFormat, commonObjectFileFormatAndLTCG }

/**
 * The output verbosity level.
 */
@@public
export const enum Verbosity { full, none, reportUnusedLibraries }

/**
 * Arguments for lib.exe
 */
@@public
export interface Arguments extends Transformer.RunnerArguments {
    /**
     * Removes the specified default library(s) from the list of libraries it searches when resolving external references.
     * Only applies to Import type libraries.
     */
    defaultLibrariesToIgnore?: PathAtom[];

    /** Disables the warning numbers that are specified in a comma-delimited list. */
    disableSpecificWarnings?: number[];

    /**
     * Specifies what to export on the command line.  Multiples allowed.
     * Only applies to Import type libraries.
     */
    exports?: Link.ExportSymbol[];

    /**
     * Specifies a value indicating to the librarian to ignore all default libraries.
     * Only applies to Import type libraries.
     */
    ignoreAllDefaultLibraries?: boolean;

    /**
     * Specifies symbols to include in the symbol table.
     * Only applies to Import type libraries.This would be used to force the librarian to resolve an otherwise unreferenced symbol, which
     * might then pull in something that alters how the import library is linked.  For example, if an
     * otherwise unreferenced OBJ file contains an "/EXPORT" directive, that could be pulled in, adding
     * to the exports.
     */
    includeSymbols?: string[];

    /** Controls which input formats are supported. */
    inputFormat?: InputFormat;

    /** A list of .lib files to add to the sources */
    libraries?: File[];

    /** If this flag is set, export libraries will be generated and allows exports to come from __declspec export. */
    libraryType?: LibraryType;

    /**
     * Specifies the .def file used to generate import and export libraries.
     * Only applies to Import type libraries.
     */
    moduleDefinitionFile?: File;

    /**
     * When building an import library, specifies the name of the DLL for which the import library is being built.
     * Only applies to Import type libraries.
     */
    moduleName?: PathAtom;

    /** Overrides the default name and location of the program that lib.exe creates. */
    outputFileName?: PathAtom;

    /** Specifies the target platform for the program. */
    platform?: Shared.Platform;

    /** List of resource files (.res) */
    resources?: File[];

    /**
     * Add a response file with a set of common defines.
     * This response file will be added at the start of the command line.
     */
    @@Tool.option("@{responseFile}")
    responseFile?: File;

    /**
     * List of ICompilationOutputs object to act upon
     * This command creates a library from one or more input files. The files can be COFF object files, 32-bit OMF object
     * files, or existing COFF libraries. LIB creates one library that contains all objects in the specified files. If an
     * input file is a 32-bit OMF object file, LIB converts it to COFF before building the library. LIB cannot accept a 32-bit
     * OMF object that is in a library created by the 16-bit version of LIB. You must first use the 16-bit LIB to extract the
     * object; then you can use the extracted object file as input to the 32-bit LIB.
     */
    sources?: Cl.CompilationOutput[];

    /** Suppress copyright message */
    suppressStartupBanner?: boolean;

    /** Treats all librarian warnings as errors. */
    treatWarningAsError?: boolean;

    /** Displays details about the progress of the session, including names of the .obj files being added. */
    verbosity?: Verbosity;
}

/**
 * Output of running the lib transformer
 */
@@public
export interface Result {
    /** The static library created by lib.exe */
    binaryFile: File;

    /** The export file that needs to be passed to the linker (created only if LibraryType.import) */
    exportFile?: File;
}

/**
 * The type of library to produce.
 */
@@public
export const enum LibraryType { importType, staticType }

/**
 * Type:Runner for the tool:LIB.EXE Description: It is a tool that creates and manages a library of Common Object File Format (COFF) object files. LIB can also be used to
 * create export files and import libraries to reference exported definitions.. lib.exe is a process belonging to Microsoft® Linker Stub
 */
@@Tool.runner("link -lib")
@@public
export function evaluate(args: Arguments): Result {
    Contract.requires(args.outputFileName !== undefined, "'outputFileName' not specified");
    if (args.libraryType === LibraryType.staticType) {
        Contract.requires(args.moduleDefinitionFile === undefined, "Can't specify DEF file with LibraryType.static");
        Contract.requires(args.exports.length === 0, "Can't specify exports with LibraryType.static");
        Contract.requires(!args.ignoreAllDefaultLibraries && args.defaultLibrariesToIgnore.length === 0, "Can't specify ignore libraries with LibraryType.static");
        Contract.requires(args.includeSymbols.length === 0, "Can't specify include symbols with LibraryType.static");
    }

    args = defaultLibArguments.override<Arguments>(args);

    let outDir = Context.getNewOutputDirectory("link");
    let outFile = outDir.combine(args.outputFileName);
    let expOutFile = args.libraryType === LibraryType.importType ? outFile.changeExtension(".exp") : undefined;

    let cmdArgs = [
        Cmd.flag("/NOLOGO", args.suppressStartupBanner),
        Cmd.option("@", Artifact.input(args.responseFile)),
        Cmd.startUsingResponseFile(false),

        Cmd.flag("/DEF", args.libraryType === LibraryType.importType && args.moduleDefinitionFile === undefined),
        Cmd.option("/DEF:", Artifact.input(args.moduleDefinitionFile)),
        Cmd.option("/OUT:", Artifact.output(outFile)),

        // default lib options
        Cmd.flag("/NODEFAULTLIB", args.ignoreAllDefaultLibraries),
        Cmd.options("/NODEFAULTLIB:", args.defaultLibrariesToIgnore),

        // common
        Cmd.option("/MACHINE:", Shared.mapEnumConst(args.platform,
            [Shared.Platform.arm32, "ARM"],
            [Shared.Platform.arm64, "ARM64"],
            [Shared.Platform.x64, "X64"],
            [Shared.Platform.x86, "X86"])),
        Cmd.flag("/LTCG", args.inputFormat === InputFormat.commonObjectFileFormatAndLTCG),
        Cmd.flag("/WX", args.treatWarningAsError),
        Cmd.options("/IGNORE:", args.disableSpecificWarnings),
        Cmd.option("/VERBOSE", Shared.mapEnumConst(args.verbosity,
            [Verbosity.none, undefined],
            [Verbosity.full, ""],
            [Verbosity.reportUnusedLibraries, ":unusedlibs"])),
        Cmd.option("/NAME:", args.moduleName),

        // input library and object files
        Cmd.args(Artifact.inputs(args.sources.map(s => s.binary.object))),
        Cmd.args(Artifact.inputs(args.resources)),
        Cmd.args(Artifact.inputs(args.libraries)),

        // symbols
        Cmd.options("/INCLUDE:", args.includeSymbols),

        // exports
        Cmd.options("/EXPORT:", args.exports.map(e => {
            let ordStr = (e.ordinal && e.ordinal !== 0) ? ",@" + e.ordinal.toString() : "";
            let hideStr = e.hideName ? ",NONAME" : "";
            let dataStr = e.symbolType === Link.ExportSymbolType.dataType ? ",DATA" : "";
            return e.name + ordStr + hideStr + dataStr;
        })),
    ];

    let result = Transformer.execute({
        tool: args.tool || importFrom("VisualCpp").libTool,
        workingDirectory: outDir,
        tags: args.tags,
        arguments: cmdArgs,
        dependencies: [],
        implicitOutputs: expOutFile ? [ expOutFile ] : [],
        environmentVariables: []
    });

    return <Result>{
        binaryFile: result.getOutputFile(outFile),
        exportFile: result.getOutputFile(expOutFile)
    };
}
