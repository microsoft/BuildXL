// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";
import * as Shared from "Sdk.Managed.Shared";
import * as Csc from "Sdk.Managed.Tools.Csc";

/**
 * User-centric set of arguments for running crossgen.
 */
@@public
export interface Arguments extends Transformer.RunnerArguments{
    /** Prevents displaying the logo. */
    noLogo?: boolean;

    /** Prevents displaying warning messages. */
    noWarnings?: boolean;

    /** Do not display completion message. */
    silent?: boolean;

    /** Displays verbose information. */
    verbose?: boolean;

    /** Managed binary to run crossgen on. */
    inputBinary: Shared.Binary;

    /** Name of the ready-to-run binary to produce. 
     * Same as the input binary if not specified. */
    outputName?: PathAtom;

    /** Collection of trusted platform assembly references. */
    references?: Shared.Binary[];

    /** Specifies that crossgen should attempt not to fail if a dependency is missing. */
    missingDependenciesOk?: boolean;

    /** Specifies the absolute file path to JIT compiler to be used. */
    JITPath?: File;

    /** The runtime version crossgen is targeted at. */
    targetRuntime: Shared.RuntimeVersion;

    /** The framework crossgen is targeted at. */
    targetFramework: Shared.Framework;
}

function tool(crossgenExe: File): Transformer.ToolDefinition {
    return Shared.Factory.createTool({
        description: "CoreCLR Native Image Generator",
        exe: crossgenExe,
        runtimeDependencies: [],
        prepareTempDirectory: true
    });
}

@@public
export function defaultArgs(JITPath: File): Arguments {
    return {
        noLogo: true,
        noWarnings: false,
        silent: false,
        verbose: false,
        inputBinary: undefined,
        outputName: undefined,
        references: undefined,
        missingDependenciesOk: true,
        JITPath: JITPath,
        targetRuntime: undefined,
        targetFramework: undefined,
    };
};

/**
 * Evaluate (i.e. schedule) crossgen invocation using specified arguments.
 */
@@Tool.runner("crossgen.exe")
@@public
export function crossgen(inputArgs: Arguments) : Shared.Binary {
    Contract.assert(inputArgs.targetFramework.crossgenProvider !== undefined, "The provided framework does not support running crossgen");

    const crossGenFiles = inputArgs.targetFramework.crossgenProvider(inputArgs.targetRuntime);

    const args = defaultArgs(crossGenFiles.JITPath).merge<Arguments>(inputArgs);

    let output: PathAtom = args.outputName;

    // If the output is not explicitly specified, we use the same name of the input binary
    if (output === undefined){
        output = args.inputBinary.binary.name;
    }

    const outputDirectory = Context.getNewOutputDirectory(output + "-crossgen");
    const outputBinPath = outputDirectory.combine(output);
    
    // We need to add all runtime files for the current target runtime as references
    const references = [
        ...inputArgs.targetFramework.runtimeContentProvider(args.targetRuntime),
        ...(args.references && args.references.map(r => r.binary))
        ];

    let crossgenArguments: Argument[] = [
        Cmd.flag("/nologo",                 args.noLogo),
        Cmd.flag("/nowarnings",             args.noWarnings),
        Cmd.flag("/silent",                 args.silent),
        Cmd.flag("/verbose",                args.verbose),

        Cmd.startUsingResponseFile(),

        Cmd.option("/in ",                  Artifact.input(args.inputBinary.binary)),
        Cmd.option("/out ",                 Artifact.output(outputBinPath)),
        Cmd.options("/r ",                  Artifact.inputs(references)),
        Cmd.flag("/MissingDependenciesOK",  args.missingDependenciesOk),
        Cmd.option("/JITPath ",             Artifact.input(args.JITPath)),
    ];

    let crossgenExecuteArgs : Transformer.ExecuteArguments = {
        tool: args.tool || tool(crossGenFiles.crossgenExe),
        arguments: crossgenArguments,
        workingDirectory: outputDirectory,
        tags: ["crossgen"],
        errorRegex: "error.*",
        // Even though we are preparing a temp directory, crossgen tool insists in
        // generating a temporary file in the output directory
        outputs: [{
            artifact: outputBinPath.changeExtension(a`.dll.tmp`), 
            existence: "temporary"}]
    };

    let executeResult = Transformer.execute(crossgenExecuteArgs);

    // Compose result object. Optional pdb and doc (if present) should be the same of
    // the original binary
    const binary = Shared.Factory.createBinaryFromFiles(
        executeResult.getOutputFile(outputBinPath),
        args.inputBinary.pdb,
        args.inputBinary.documentation
    );

    return binary;
}
