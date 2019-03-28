// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

import {Shared, PlatformDependentQualifier} from "Sdk.Native.Shared";

export declare const qualifier: PlatformDependentQualifier;

/**
 * Bin extension path atom
 */
const binExtension = PathAtom.create(".bin");

/**
 * Message Compiler basic options.
 */
@@public
export interface McOptions {
    /**
     * Base name used by the compiler for the files that it generates.
     * Default is the base name of the input file.
     */
    @@Tool.option("-z", { optionSeparationMode: Tool.OptionSeparationMode.required, optionSeparator: " " })
    baseFileName?: PathAtom;

    /**
     * Set the customer bit (bit 28) in all message IDs.
     * For information on the customer bit, see winerror.h
     */
    @@Tool.option("-c")
    customerBit?: boolean;

    /**
     * Specify the extension to use for the header file.
     * You can specify up to three characters extension not including the period.  The default is .h.
     */
    @@Tool.option("-e", { optionSeparationMode: Tool.OptionSeparationMode.required, optionSeparator: " " })
    fileNameExtension?: PathAtom;

    /** Number of languages defined in the input files. */
    languagesCount?: number;

    /** Generate a warning if any message exceeds this length.  Zero means no limit. */
    @@Tool.option("-m", { optionSeparationMode: Tool.OptionSeparationMode.required, optionSeparator: " " })
    maximumMessageLength?: number;
}

/**
 * Default message compiler options
 */
@@public
export const defaultMcOptions: McOptions = {
    customerBit: true,
    maximumMessageLength: 0,
    baseFileName: undefined,
    fileNameExtension: undefined,
    languagesCount: 1
};

/**
 * The prefix rule to use to name the .bin file
 */
@@public
export const enum BinFileNamingPrefix {
    /** The default is to use "MSG" */
    defaultOption,
    /** Use the base file name to construct the .bin file names. */
    @@Tool.option("-b")
    useBaseFileName
}

/**
 * Use a specific base numbering system for for the Severity and Facility constants in the header.
 */
@@public
export const enum ConstantsEncoding {
    /** Use decimal values for the Severity and Facility constants in the header instead of hexadecimal values. */
    @@Tool.option("-d")
    decimal,
    /** Use decimal values for the Severity and Facility constants in the header.  This is the default. */
    hexadecimal
}

/**
 * Generate an OLE2 header file using HRESULT definitions instead of the legacy status codes.
 */
@@public
export const enum ConstantsType {
    /** Use HRESULT type for constants. */
    @@Tool.option("-o")
    hRESULT,
    /** Use SCODE type for constants. Default. */
    sCODE
}

/**
 * The source file content encoding.
 */
@@public
export const enum InputContentEncoding {
    /** The input file contains ANSI content. This is the default. */
    @@Tool.option("-a")
    aNSI,
    /** The output file contains Unicode content. */
    @@Tool.option("-u")
    unicode
}

/**
 * The termination to apply to each message string.
 */
@@public
export const enum MessageTermination {
    /** Add a carriage return linefeed pair to the end of each message string. */
    cRLF,
    /** Do not add any folding whitespace termination to messages. */
    @@Tool.option("-n")
    none
}

/**
 * Specify what encoding to write content to the .bin file should be.
 */
@@public
export const enum OutputContentEncoding {
    /** The input file contains ANSI content. */
    @@Tool.option("-A")
    aNSI,
    /** The output file contains Unicode content.  This is the default. */
    @@Tool.option("-U")
    unicode
}

/**
 * The message text file compiler options.
 */
export interface McHeaderOptions extends McOptions {
    /**
     * Use the base file name to construct the .bin file names.
     * The default is to use "MSG"
     */
    binFileNaming: BinFileNamingPrefix;

    /** Use a specific base numbering system for the Severity and Facility constants in the header. */
    constantsEncoding: ConstantsEncoding;

    /** Generate an OLE2 header file using HRESULT definitions instead of the legacy status codes. */
    constantsType: ConstantsType;

    /** Specifies Denotes that the content in the source is Unicode content. */
    inputContentEncoding: InputContentEncoding;

    /** The termination to apply to each message string. */
    messageTermination: MessageTermination;

    /** Specify what encoding to write content to the .bin file should be. */
    outputContentEncoding: OutputContentEncoding;
}

/**
 * The arguments to mc.exe.
 */
@@public
export interface Arguments extends McHeaderOptions, Transformer.RunnerArguments {
    /** The list of message or manifest files to compile. */
    sources?: Shared.SourceFileArtifact[];
}

/**
 * The default message compiler options
 */
export const defaultArgs: Arguments = defaultMcOptions.override<Arguments>({
    binFileNaming: BinFileNamingPrefix.defaultOption,
    inputContentEncoding: InputContentEncoding.aNSI,
    outputContentEncoding: OutputContentEncoding.aNSI,
    constantsEncoding: ConstantsEncoding.hexadecimal,
    constantsType: ConstantsType.hRESULT,
    messageTermination: MessageTermination.cRLF,
});

/**
 * The value produced by the Mc transformer.
 */
interface WorkContext {
    /** The C/C++ header file (.h) generated */
    header?: File;

    /**
     * List of binary resource files that contains one resource file for each language
     * specified in the manifest.
     */
    languageBinaryResources?: File[];

    /** The resource compiler script that contains the statements to include each binary file as a resource. */
    resourceCompilerScript: File;
}

/**
 * The result per run of the message compiler
 */
@@public
export interface McOutput {
    /** The C/C++ header file (.h) generated */
    header: StaticDirectory;

    /**
     * List of binary resource files that contains one resource file for each language
     * specified in the manifest.
     */
    languageBinaryResources: StaticDirectory;

    /** The resource compiler script that contains the statements to include each binary file as a resource. */
    resourceCompilerScript: File;
}

/**
 * Determines if the file is an mc file
 */
@@public
export function isMcFile(source: Shared.SourceFileArtifact): boolean {
    return typeof source === "File" && (source as File).extension === a`.mc`;
}

/**
 * Default tool
 */
export function defaultTool(): Transformer.ToolDefinition {
    //TODO: Need to have a downloadable  package for the Windows SDk.
    Contract.fail("No default tool was provided");
    return undefined;
}

/**
 * The message compiler (mc.exe) is used to compile instrumentation manifests and
 * message text files to generate the resources files to which an application links.
 */
@@Tool.runner("mc.exe (mc)")
@@public
export function evaluate(args: Arguments) : Map<PathAtom, McOutput> {
    Contract.requires(args.baseFileName === undefined || !args.baseFileName.hasExtension,
        "If the base file name is defined it cannot have an extension baseFileName:" + args.baseFileName);

    args = defaultArgs.override<Arguments>(args);

    let tool = args.tool || defaultTool();

    let results = args.sources.reduce(
        (acc, src, idx) => {
            let outDir = Context.getNewOutputDirectory("mc");

            let cmdArgs: Argument[] = [
                Cmd.option("-r ", Artifact.none(outDir)),
                Cmd.option("-h ", Artifact.none(outDir)),
                Cmd.option("-z ", args.baseFileName, args.baseFileName !== undefined),
                Cmd.flag("-d", args.constantsEncoding === ConstantsEncoding.decimal),
                Cmd.flag("-o", args.constantsType === ConstantsType.hRESULT),
                Cmd.flag("-c", args.customerBit),
                Cmd.option("-m ", args.maximumMessageLength, args.maximumMessageLength !== 0),
                Cmd.flag("-n", args.messageTermination === MessageTermination.none),
                Cmd.argument(Artifact.input(Shared.getFile(src)))
            ];

            let baseName = (args.baseFileName !== undefined ? args.baseFileName : Shared.getFile(src).nameWithoutExtension);
            let header = createOutputFile(outDir.path, baseName, (args.fileNameExtension !== undefined ? args.fileNameExtension : ".h"));
            let resourceCompilerScript = createOutputFile(outDir.path, baseName, ".rc");

            let languageBinaryResources: File[] = [];

            let languageCount = (args.languagesCount > 0 ? args.languagesCount : 1);

            for (let i = 1; i <= languageCount; i++) {
                let baseMsgFileName = "MSG" + Shared.prepend("0", 5, i.toString()); // i.toString("D5")
                let languageBaseName = (args.baseFileName !== undefined ?
                                            PathAtom.interpolate(args.baseFileName, "_" + baseMsgFileName) :
                                            PathAtom.create(baseMsgFileName));
                let outputLanguageBinFile = createOutputFile(outDir.path, languageBaseName, binExtension);

                languageBinaryResources = languageBinaryResources.push(outputLanguageBinFile);
            }

            let workContext : WorkContext = {
                header: header,
                resourceCompilerScript: resourceCompilerScript,
                languageBinaryResources: languageBinaryResources
            };

            let transformerResult = Transformer.execute({
                tool: tool,
                dependencies: [],
                workingDirectory: Context.getSpecFileDirectory(),
                arguments: cmdArgs,
                implicitOutputs: [header, resourceCompilerScript, ...languageBinaryResources]
            });

            let mcOutput = createMcOutput(workContext, transformerResult);
            let sourceFileName = Shared.getFile(src).path.name;
            return acc.add(sourceFileName, mcOutput);
        },
        Map.empty<PathAtom, McOutput>());

    return results;
}

/**
 * Creates the Mc Output structure from the work context and the result from the transformer execution
 */
function createMcOutput(
    workContext : WorkContext,
    transformerResult : Transformer.ExecuteResult) : McOutput {
    let headerDirectory = Transformer.sealPartialDirectory(
        d`${workContext.header.parent}`,
        [ transformerResult.getOutputFile(workContext.header.path) ]);

    let languageBinaryResourcesDirectory = Transformer.sealPartialDirectory(
        d`${workContext.resourceCompilerScript.parent}`,
        [ transformerResult.getOutputFile(workContext.resourceCompilerScript.path) ]
            .concat(workContext.languageBinaryResources.map(item => transformerResult.getOutputFile(item.path))));

    return {
        header: headerDirectory,
        languageBinaryResources: languageBinaryResourcesDirectory,
        resourceCompilerScript: transformerResult.getOutputFile(workContext.resourceCompilerScript.path) as File,
    };
}

/**
 * Function that creates a path with a directory a base name and an extension.
 * Removes the extension from the base name if it has one
 * 
 * O:/Absolute/Path/To/Output/ baseName.ext .exe -> O:/Absolute/Path/To/Output/baseName.exe
 */
function createOutputFile(
    outputDirectory: Path,
    outputBaseName: PathAtom,
    extension: PathAtomOrString) : File {
    let outputFileName : PathAtom = outputBaseName.changeExtension(extension);
    let outputFile = f`${outputDirectory}/${outputFileName}`;
    return outputFile;
}
