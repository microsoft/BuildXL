// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import * as Managed from "Sdk.Managed";

/**
 * Arguments for LogGenenerator
 */
@@public
export interface Arguments extends Transformer.RunnerArguments {
    /** Namespace */
    generationNamespace: string;

    /** The output filename */
    outputFile: string;

    /** The references. */
    references?: Managed.Binary[];

    /** Input source files to compile. */
    sources?: File[];

    /** Preprocessor symbols for compilation */
    defines?: string[];

    /** Aliases for predefined string replacements in log messages */
    aliases?: {key: string, value: string}[];

    /** The targetFramework to generate code for */
    targetFramework: string;

    /** The targetRuntime to generate code for */
    targetRuntime: string;
}

// Default arguments for running Log Generator
export const defaultArgs: Arguments = {
    generationNamespace: undefined,
    outputFile: undefined,
    references: [],
    sources: [],
    targetFramework: "netcoreapp3.0",
    targetRuntime: "win-x64",
};

// Runs Log Generator
@@public
export function generate(inputArgs: Arguments): File {
    const args = defaultArgs.override<Arguments>(inputArgs);

    const outputFolder = Context.getNewOutputDirectory("loggen");
    const outFile = outputFolder.combine(args.outputFile);

    const commandLineArgs: Argument[] = [
        Cmd.startUsingResponseFile(),
        Cmd.options(
            "/s:",
            Artifact.inputs(args.sources)
        ),
        Cmd.options(
            "/r:",
            Artifact.inputs(
                args.references.map(r => r.binary)
            )
        ),
        Cmd.option("/namespace:", args.generationNamespace),
        Cmd.option("/targetFramework:", args.targetFramework),
        Cmd.option("/targetRuntime:", args.targetRuntime),
        Cmd.option(
            "/output:",
            Artifact.output(outFile)
        ),
        Cmd.option(
            "/d:",
            args.defines ? args.defines.join(";") : undefined
        ),
        Cmd.options(
            "/a:",
            args.aliases ? args.aliases.map(kv => `${kv.key}=${kv.value}`) : undefined
        ),
    ];

    const result = Transformer.execute(
        {
            tool: importFrom("BuildXL.Utilities.Instrumentation").LogGen.withQualifier(Managed.TargetFrameworks.currentMachineQualifier).tool,
            arguments: commandLineArgs,
            workingDirectory: outputFolder,
        }
    );

    return result.getOutputFile(outFile);
}
