// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import * as BuildXLSdk from "Sdk.BuildXL";

export declare const qualifier : BuildXLSdk.FullFrameworkQualifier;

/**
 * Usage for reference. NOTE: Not all functionality is exposed by this transformer.
 * >bondc.exe /?
 * USAGE:
 * bondc.exe [/R:ReferencePath]* [/{ParamName}:{ParamValue}]* /T:TemplateName
 *           [/O:OutputDirectory] [/N:SuppressCode]* [/I:path]* [/L:FilesList] bondFile
 *           [bondFileNew]
 *
 *    /I   - Adds a directory to the list of directories used to search imported
 *           files.
 *    /N   - Suppress messages with specified code.
 *    /O   - Target files will be generated in the specified target directory.
 *           If omitted, then current directory is used.
 *    /R   - Specify a reference path for assembly references
 *    /T   - Specify a T4 Template to apply
 *           (generally *.tt or AssemblyFileName.dll#ResourceName)
 *    /L   - Specify a text file containing file names of bond files
 *    /NoToolInfo - Suppress some tool info in generated files.
 *    /{ParamName}:{ParamValue}   - Custom parameters for the specified Template.
 *
 * Some frequently used templates have shorter aliases:
 *    /c++      - generates C++ code from Bond IDL file bondFile.
 *    /c#       - generates C# code from Bond IDL file bondFile.
 *    /java     - generates Java code from Bond IDL file bondFile.
 *    /schema   - generates runtime schema from Bond IDL file bondFile.
 *    /validate - validate schema changes between bondFile and bondFileNew
 *
 * Examples:
 *    bondc /c# example.bond
 *      - generates C# code from example.bond. Generated files go into current dir
 *
 *    bondc /c++ example.bond
 *      - generates C++ code from example.bond. Generated files go into current dir
 *
 *    bondc /R:. /T:Microsoft.BondRules.dll#Rules_Bond_CSharp.tt
 *            /IsCyprusType=False /O:objd/amd64 example.bond
 *      - generates code using specified template (Rules_Bond_CSharp.tt)
 *        from the assembly Microsoft.BondRules.dll.
 *        Generated files go into objd/amd64.
 */

const pkgContents : StaticDirectory = importFrom("Bond.NET").Contents.all;

@@public
export const tool: Transformer.ToolDefinition = !BuildXLSdk.Flags.isMicrosoftInternal ? undefined : {
    exe: pkgContents.getFile(r`bin/bondc.exe`),
    description: "Bond Code Generator",
    runtimeDependencies: pkgContents.getFiles([
        r`bin/Microsoft.Bond.Rules.dll`,
        r`bin/Microsoft.VisualStudio.TextTemplating.dll`
        ]),
    dependsOnWindowsDirectories: true,
    dependsOnAppDataDirectory: true,
    prepareTempDirectory: true
};

@@public
export function generate(args: Arguments) : Result {

    let outputDirectory = Context.getNewOutputDirectory("bond");
    let includeDirs = (args.includeFiles || []).map(f => f.parent).unique();

    let customArgs: Argument[] = args.customParameters
        ? args.customParameters.map(customParameter => Cmd.option("/" + customParameter.name + ":", customParameter.value))
        : [];

    let templateType = args.templateType || "csharp";

    let arguments : Argument[] = [
        ...customArgs,
        Cmd.options("/R:", Artifact.inputs(args.referencePaths)),
        Cmd.option("/O:", Artifact.none(outputDirectory)),
        Cmd.option("/N:", args.suppressCodes && args.suppressCodes.join(",")),
        Cmd.options("/I:", includeDirs.map(Artifact.none)),
        Cmd.option("/L:", Artifact.input(args.listFile)),
        Cmd.flag("/NoToolInfo:", args.noToolInfo),
        Cmd.argument(templateTypeToArgument(templateType)),
        Cmd.argument(Artifact.input(args.bondFile)),
    ];

    let bondFileWithoutExtension = args.bondFile.nameWithoutExtension;


    switch(templateType) {
        case "csharp": {
            let proxiesFile = p`${outputDirectory}/${bondFileWithoutExtension + "_proxies.cs"}`;
            let servicesFile = p`${outputDirectory}/${bondFileWithoutExtension + "_services.cs"}`;
            let typesFile = p`${outputDirectory}/${bondFileWithoutExtension + "_types.cs"}`;
            let rulesFile = p`${outputDirectory}/Microsoft.Bond.Rules.dll#Rules_Bond_CSharp.txt`;
            let result = Transformer.execute({
                tool: args.tool || tool,
                arguments: arguments,
                workingDirectory: outputDirectory,
                outputs: [proxiesFile, servicesFile, typesFile, rulesFile],
                dependencies: args.includeFiles || []
            });
            return {
                csharpResult: {
                    proxiesFile: result.getOutputFile(proxiesFile),
                    servicesFile: result.getOutputFile(servicesFile),
                    typesFile: result.getOutputFile(typesFile),
                },
                rulesFile: result.getOutputFile(rulesFile),
            };
        }
        case "cpp": {
            let typesHeaderFile = p`${outputDirectory}/${bondFileWithoutExtension + "_types.h"}`;
            let typesFile = p`${outputDirectory}/${bondFileWithoutExtension + "_types.cpp"}`;
            let reflectionHeaderFile = p`${outputDirectory}/${bondFileWithoutExtension + "_reflection.h"}`;
            let applyHeaderFile = p`${outputDirectory}/${bondFileWithoutExtension + "_apply.h"}`;
            let applyFile = p`${outputDirectory}/${bondFileWithoutExtension + "_apply.cpp"}`;
            let rpcHeaderFile = p`${outputDirectory}/${bondFileWithoutExtension + "_rpc.h"}`;
            
            let rulesFile = p`${outputDirectory}/Microsoft.Bond.Rules.dll#Rules_BOND_CPP.txt`;

            let result = Transformer.execute({
                tool: args.tool || tool,
                arguments: arguments,
                workingDirectory: outputDirectory,
                outputs: [typesHeaderFile, typesFile, reflectionHeaderFile, applyHeaderFile, applyFile, rpcHeaderFile, rulesFile],
                dependencies: args.includeFiles || []
            });
            return {
                cppResult: {
                    typesHeaderFile: result.getOutputFile(typesHeaderFile),
                    typesFile: result.getOutputFile(typesFile),
                    reflectionHeaderFile: result.getOutputFile(reflectionHeaderFile),
                    applyHeaderFile: result.getOutputFile(applyHeaderFile),
                    applyFile: result.getOutputFile(applyFile),
                    rpcHeaderFile: result.getOutputFile(rpcHeaderFile),
                },
                rulesFile: result.getOutputFile(rulesFile)
            };
        }
        default:
            return Contract.fail("Unexpected TemplateType '" + args.templateType + "'.");
    }

}

@@public
export interface Arguments extends Transformer.RunnerArguments{
    /** Bond file to use for code generation. */
    bondFile: File;

    /** Directories to add to the list of directories used to search imported files. */
    includeFiles?: File[];

    /** Specify the type to use */
    templateType?: "csharp" | "cpp";

    /** Suppress messages with specified code. */
    suppressCodes?: string[];

    /** Specify a reference path for assembly references. */
    referencePaths?: File[];

    /** Specify a text file containing file names of bond files. */
    listFile?: File;

    /** Suppress some tool info in generated files. */
    noToolInfo?: boolean;

    /** Custom parameters for the specified Template. */
    customParameters?: NameValuePair[];
}

@@public
export interface Result {
    csharpResult?: CSharpResult;
    cppResult?: CppResult;
    rulesFile: File;
}

@@public
export interface CSharpResult {
    proxiesFile: File;
    servicesFile: File;
    typesFile: File;
}

@@public
export interface CppResult {
    typesHeaderFile: File;
    typesFile: File;
    reflectionHeaderFile: File;
    applyHeaderFile: File;
    applyFile: File;
    rpcHeaderFile: File;
}

/**
 * Simple name-value pair interface used for parsing custom parameters.
 */
@@public
export interface NameValuePair {
    name: string;
    value: string;
}

function templateTypeToArgument(templateType: string): string {
    switch(templateType) {
        case "csharp":
            return "/c#";
        case "cpp":
            return "/c++";
        default:
            return Contract.fail("Unexpected TemplateType '" + templateType + "'.");
    }
}
