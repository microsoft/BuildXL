// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import * as Managed from "Sdk.Managed";

namespace Tool {

    export declare const qualifier : Managed.TargetFrameworks.CurrentMachineQualifier;

    const NetFx = qualifier.targetFramework === "net472" ? importFrom("Sdk.Managed.Frameworks.Net472").NetFx : undefined;

    const resGenLite = Managed.executable({
        assemblyName: "ResGen.Lite",
        sources: globR(d`.`, "*.cs"),
        references: [
            ...(qualifier.targetFramework === "net472" ? [
                NetFx.System.Xml.dll,
                NetFx.System.Xml.Linq.dll,
                importFrom("System.Collections.Immutable").pkg,
                importFrom("System.Reflection.Metadata").pkg,
                importFrom("System.Threading.Tasks.Extensions").pkg
            ] : []),

            // CodeAnalysis packages come with .NETStandard assemblies only. Force netstandard2.0 here as .NET 4.7.2 is
            // compatible and the x-plat builds need that flavor anyway
            importFrom("Microsoft.CodeAnalysis.Common").withQualifier({ targetFramework: "netstandard2.0" }).pkg,
            importFrom("Microsoft.CodeAnalysis.CSharp").withQualifier({ targetFramework: "netstandard2.0" }).pkg,
            importFrom("Microsoft.CodeAnalysis.CSharp.Workspaces").withQualifier({ targetFramework: "netstandard2.0" }).pkg,
            importFrom("Microsoft.CodeAnalysis.Workspaces.Common").withQualifier({ targetFramework: "netstandard2.0" }).pkg,

            importFrom("System.Composition.AttributedModel").withQualifier({ targetFramework: "netstandard1.0" }).pkg,
            importFrom("System.Composition.Convention").withQualifier({ targetFramework: "netstandard1.0" }).pkg,
            importFrom("System.Composition.Hosting").withQualifier({ targetFramework: "netstandard1.0" }).pkg,
            importFrom("System.Composition.Runtime").withQualifier({ targetFramework: "netstandard1.0" }).pkg,
            importFrom("System.Composition.TypedParts").withQualifier({ targetFramework: "netstandard1.0" }).pkg,
        ],
        tools: {
            csc: {
                noWarnings: [1701, 1702], // Disable assembly mismatch warnings.
            }
        }
    });

    export const tool = Managed.deployManagedTool({
        tool: resGenLite,
        description: "ResGen.Lite",
    });
}

@@public
export function generate(args: Arguments): Result {
    const outputDirectory = Context.getNewOutputDirectory("resgen");

    const resourcesFileName = args.outputFileName || args.inputFile.path.name.changeExtension(".resources");;
    const outputResourceFile = p`${outputDirectory}/${resourcesFileName}`;

    const generatedClass = args.generatedClass;
    let generatedSourceFile : Path = undefined;
    let generatedSourceArgument : string = undefined;

    if (generatedClass){
        const name = generatedClass.className
        ? generatedClass.className
        : args.inputFile.path.nameWithoutExtension;

        const fileName = generatedClass.fileName || name + ".g." + generatedClass.language;

        generatedSourceFile = p`${outputDirectory}/${fileName}`;
        generatedSourceArgument = [
            generatedClass.language,
            generatedClass.nameSpace,
            generatedClass.className,
            fileName
        ].filter(x => x !== undefined).join(",");
    }

    const arguments: Argument[] = [
        Cmd.flag("-publicClass",   args.publicClass),
        Cmd.option("-str:",        generatedSourceArgument),
        Cmd.argument(Artifact.input(args.inputFile)),
        Cmd.argument(Artifact.output(outputResourceFile))
    ];

    const result = Transformer.execute({
        tool: args.tool || Tool.withQualifier(Managed.TargetFrameworks.currentMachineQualifier).tool,
        arguments: arguments,
        workingDirectory: outputDirectory,
        // because sourceFile could be undefined we need to filter both: dependencies and expected outputs.
        implicitOutputs: generatedSourceFile !== undefined ? [ generatedSourceFile ] : [],
        tags: ["codegen"]
    });

    return {
        resourceFile: {
            file: result.getOutputFile(outputResourceFile),
            isPublic: args.isPublic,
            logicalName: args.logicalName
        },
        sourceFile: generatedSourceFile && result.getOutputFile(generatedSourceFile),
    };
}

//-----------------------------------------------------------------------------
//  Arguments
//-----------------------------------------------------------------------------

/**
 * The arguments to resgen.exe.
 */
@@public
export interface Arguments extends Transformer.RunnerArguments {
    /**
     * The input resource file to convert from.
     * Valid files types are .resX, .restext, .txt and .resources.
     */
    inputFile: File;

    /** True if the resulting .resources should be public to the assembly, otherwise False. */
    isPublic?: boolean;

    /** The name of the resulting resource as it should be mapped in to consumers. */
    logicalName?: string;

    /** Specifies the name of the output resource file (including extension) to convert to. */
    outputFileName?: string;

    /** Create the strongly typed resource class as a public class. */
    publicClass?: boolean;

    /** The strongly typed class description to use for the generated source file. */
    generatedClass?: StronglyTypedResourceClass;
}

@@public
export interface StronglyTypedResourceClass {
    /**
     * The language to use to generate the source file.
     *
     * Language names valid for the /str:<language> option are:
     * c#, cs, csharp, vb, vbs, visualbasic, vbscript, js, jscript, javascript, c++, mc, cpp
     */
    language: Language;

    /**
     * The Namespace to use for the generate source file.
     *
     * If specified, Language must be specified too.
     * Creates a strongly-typed resource class in the specified
     *  programming language. In order for the strongly
     *  typed resource class to work properly, the name of your output
     *  file without the .resources must match the
     *  [namespace.]classname of your strongly typed resource class.
     */
    nameSpace: string;

    /** The classname of the generated file. */
    className: string;

    /** The file name of the generated strongly type resource class file. */
    fileName?: string;
}

@@public
export type Language = "cs" | "vb" | "fs";

@@public
export interface Result {
    resourceFile?: Managed.LinkResource;
    sourceFile: File;
}

