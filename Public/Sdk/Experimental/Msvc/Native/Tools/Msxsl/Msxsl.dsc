// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

import {Shared} from "Sdk.Native.Shared";

/** 
 * Runner for the MSXSL.EXE too. 
 * 
 * It is a command line utility enables you to perform command line Extensible 
 * Stylesheet Language (XSL) transformations using the Microsoft® XSL processor.
 */
@@public
export const enum MsxslVersionType {
    /** Use MSXSL default, do not specify a value and let MSXSL decide */
    defaultOption,
    /** MSXSL version 2.6 */
    @@Tool.option("-u 2.6")
    v26,
    /** MSXSL version 3.0 */
    @@Tool.option("-u 3.0")
    v30,
    /** MSXSL version 4.0 */
    @@Tool.option("-u 4.0")
    v40
}

@@public
export interface Arguments extends Transformer.RunnerArguments {
    /** Source file, usually an XML file */
    sourceFile: File;

    /** Stylesheet file, usually a XSL or XSLT used to reformat the XML */
    styleSheet: File;

    /** Xsd file */
    xsdSchemaFile?: File;

    /** Output file path, such as a sourcename.designer.cs */
    outputFileName: PathAtom;

    /** Strip non-significant whitespace from source and stylesheet */
    @@Tool.option("-xw")
    removeWhiteSpace?: boolean;

    /** Resolve external definitions during parse phase*/
    @@Tool.option("-xe")
    resolveExternalDefinitions?: boolean;

    /** Validate documents during parse phase*/
    @@Tool.option("-v")
    validate?: boolean;

    /** Get stylesheet URL from xml-stylesheet PI in source document*/
    @@Tool.option("-pi")
    getStyleSheetUrl?: boolean;

    /** Use a specific version of MSXSL: '2.6', '3.0', or use MSXSL default */
    @@Tool.option("-u")
    version?: MsxslVersionType;
}

@@public
export interface Result {
    outFile: File;
}

export const defaultArgs: Arguments = {
    sourceFile: undefined,
    styleSheet: undefined,
    outputFileName: undefined,

    removeWhiteSpace: false,
    resolveExternalDefinitions: true,
    validate: false,
    getStyleSheetUrl: false,
    version: MsxslVersionType.defaultOption,
};

@@Tool.runner("msxsl.exe")
@@public
export function evaluate(args: Arguments): File {
    args = defaultArgs.override<Arguments>(args);

    let outDir = Context.getNewOutputDirectory("msxsl");
    let outFile = outDir.combine(args.outputFileName);

    let cmdLineArgs: Argument[] = [
        Cmd.argument(Artifact.input(args.sourceFile)),
        Cmd.argument(Artifact.input(args.styleSheet)),
        Cmd.option("-xsd ", Artifact.input(args.xsdSchemaFile)),
        Cmd.flag("-xw", args.removeWhiteSpace),
        Cmd.flag("-xe", !args.resolveExternalDefinitions),
        Cmd.flag("-v", args.validate),
        Cmd.flag("-pi", args.getStyleSheetUrl),
        Cmd.option("-u", getVersion(args.version)),
        Cmd.option("-o ", Artifact.output(outFile)),
    ];

    let transArg = {
        tool: args.tool,
        workingDirectory: outDir,
        arguments: cmdLineArgs
    };

    let pipOutputs = Transformer.execute(transArg);
    return pipOutputs.getOutputFile(outFile);
}

function getVersion(v: MsxslVersionType): string {
    switch (v) {
        case MsxslVersionType.v26: return "2.6";
        case MsxslVersionType.v30: return "3.0";
        case MsxslVersionType.v40: return "4.0";
        case MsxslVersionType.defaultOption: return undefined;
        default: return undefined;
    }
}
