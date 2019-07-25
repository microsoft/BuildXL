// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";
import * as Deployment from "Sdk.Deployment";
import * as Shared from "Sdk.Managed.Shared";

const pkgContents = importFrom("ILRepack").Contents.all;

export const tool: Transformer.ToolDefinition = {
    exe: pkgContents.getFile(r`tools/ILRepack.exe`),
    runtimeDependencies: [],
    untrackedDirectoryScopes: [],
    dependsOnWindowsDirectories: true,
    prepareTempDirectory: true,
};

@@public
export const defaultArgs: Arguments = {
    log: "ilrepack.log",
    keyfile: undefined,
    targetKind: "exe",
    xmlDocs: true,
    internalize: false,
    parallel: true,
    verbose: true,
    out: undefined,
    primaryBinary: undefined,
    targetFramework: undefined,
    otherAssemblies: []
};

/**
 * Merge multiple assemblies into a single assembly.
 */
@@public
export function repack(inputArgs: Arguments) : Shared.Binary {
    let args = defaultArgs.merge<Arguments>(inputArgs);

    const outputDirectory = Context.getNewOutputDirectory(args.out + "-ilrepack");
    const outputLogPath = args.log && p`${outputDirectory}/${args.log}`;
    const outputAssemblyPath = args.out ? p`${outputDirectory}/${args.out}` : p`${outputDirectory}/${args.primaryBinary.binary.name}`;
    const outputPdbPath = outputAssemblyPath.changeExtension(".pdb");
    const outputDocPath = outputAssemblyPath.changeExtension(".xml");

    let repackArguments: Argument[] = [
        Cmd.option("/log:",      Artifact.output(outputLogPath)),
        Cmd.option("/keyfile:",  Artifact.input(args.keyfile)),

        Cmd.option("/target:",   args.targetKind ? args.targetKind.toString() : undefined),

        Cmd.flag("/xmldocs",     args.xmlDocs),
        Cmd.flag("/internalize", args.internalize),
        Cmd.flag("/parallel",    args.parallel),
        Cmd.flag("/verbose",     args.verbose),

        Cmd.option("/out:",      Artifact.output(outputAssemblyPath)),

        ...(args.libs.map(l => Cmd.option("/lib:", Artifact.input(l)))),

        Cmd.files([args.primaryBinary.binary]),
        Cmd.files(args.otherAssemblies.map(a => a.binary))
    ];

    let executeResult = Transformer.execute({
        tool: args.tool || tool,
        arguments: repackArguments,
        workingDirectory: outputDirectory,
        dependencies: [
            args.primaryBinary.pdb,
            args.primaryBinary.documentation
        ],
        outputs: [
            outputPdbPath,
            outputDocPath
        ],
        tags: ["compile"],
    });

    const binary = Shared.Factory.createBinaryFromFiles(
        executeResult.getOutputFile(outputAssemblyPath),
        executeResult.getOutputFile(outputPdbPath),
        executeResult.getOutputFile(outputDocPath)
    );

    return binary;
}

@@public
export function repackAssembly(inputArgs: Arguments, references : Shared.Reference[], runtimeContent : Deployment.Definition) : Shared.Assembly {
    const repackedBinary = repack(inputArgs);

    return {
        name: repackedBinary.binary.name,
        targetFramework: inputArgs.targetFramework,
        compile: repackedBinary,
        runtime: repackedBinary,
        references: references,
        runtimeContent: runtimeContent,
        deploy: Shared.Deployment.flattenAssembly
    };
}

@@public
export interface Arguments extends Transformer.RunnerArguments {
    /** enable logging (to a file, if given) (default is disabled) */
    log?: string;
    /** specifies a keyfile to sign the output assembly */
    keyfile?: File;
    /** specify target assembly kind (library, exe, winexe supported, default is same as first assembly) */
    targetKind?: TargetKind;
    /** merges XML documentation as well */
    xmlDocs?: boolean;
    /** sets all types but the ones from the first assembly 'internal' */
    internalize?: boolean;
    /** use as many CPUs as possible to merge the assemblies */
    parallel?: boolean;
    /** shows more logs */
    verbose?: boolean;
    /** target assembly path, symbol/config/doc files will be written here as well */
    out?: string;
    /** The target framework the assembly is rewritten to. */
    targetFramework: string;
    /** primary assembly, gives the name, version to the merged one */
    primaryBinary: Shared.Binary;
    /** assemblies to merge in */
    otherAssemblies: Shared.Binary[];
    /** set of referenced assemblies */
    libs?: File[];
}

@@public
export type TargetKind = "library" | "exe" | "winexe";