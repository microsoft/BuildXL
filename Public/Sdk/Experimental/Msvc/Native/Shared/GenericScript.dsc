// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

namespace GenericScript {
    /**
     * Arguments to pass to the ScriptRunner
     */
    @@public
    export interface Arguments {
        /**
         * The definition of the script to be run in the tool
         */
        script: Transformer.ToolDefinition;

        /**
         * The interpreter to run the script
         */
        interpreter?: Transformer.ToolDefinition;

        /**
         * Arbitrary pip tags
         */
        tags?: string[];

        /**
         * List of files the tool depends on
         */
        dependencies?: Transformer.InputArtifact[];

        /**
         * Process environment information to pass to the PIP.
         */
        environment?: Transformer.EnvironmentVariable[];

        /**
         * List of files produced by the script.
         */
        implicitOutputs?: Transformer.OutputArtifact[];

        /**
         * Files passed into the script that are rewritten
         */
        rewrittenFiles?: File[];

        /**
         * Parameters to pass to the script.
         */
        arguments?: Argument[];

        /**
         * Parameters to pass to the script using a response file.
         */
        argumentsInResponseFile?: Argument[];

        /**
         * When true, forces the ScriptResponseFileParameters arguments into a response file. If false, then a response file
         * is only used if the command-line for the tool exceeds the maximum allowed by the system.  If not set defaults to false.
         */
        forceResponseFile?: boolean;

        /**
         * If specified, redirect the script's standard input from this file
         */
        standardInput?: File | Transformer.Data;

        /**
         * If specified, redirect the script's standard out to this file
         */
        standardOutput?: PathAtom;

        /**
         * If specified, redirect the script's standard error to this file
         */
        standardError?: PathAtom;

        /**
         * Custom regular expression for extracting warning messages from the script's outputDirectory
         */
        warningRegex?: string;

        /**
         * Directory to launch the script from.
         */
        workingDirectory?: Directory;

        /**
         * Indicates whether the script uses the TEMP directory
         */
        usesTempDirectory?: boolean;
    }

    /**
     * Run the script interpreter.
     */
    @@public
    export function evaluate(args: Arguments) : File[] {
        validateArguments(args);

        let outputDirectory = Context.getNewOutputDirectory(args.interpreter.exe.nameWithoutExtension);

        let cmdArgs = [
            ...(args.arguments || []),
            ...(args.rewrittenFiles || []).map(file => 
                Cmd.argument(
                    Artifact.rewritten(
                        file, 
                        file.path.relocate(d`${file.parent}`, outputDirectory)))),
            Cmd.startUsingResponseFile(args.forceResponseFile),
            ...(args.argumentsInResponseFile || []),
        ];

        return Transformer.execute({
            // Override the script executable to use the interpreter while preserving the dependencies of both the
            // interpreter and of the script
            tool: args.script.merge<Transformer.ToolDefinition>(args.interpreter),
            tags: args.tags,
            arguments: cmdArgs,
            workingDirectory: args.workingDirectory || outputDirectory,
            dependencies: args.dependencies || [],
            implicitOutputs: args.implicitOutputs || [],
            consoleInput: args.standardInput,
            consoleOutput: args.standardOutput && outputDirectory.path.concat(args.standardOutput),
            consoleError: args.standardError && outputDirectory.path.concat(args.standardError),
            environmentVariables: args.environment,
            warningRegex: args.warningRegex,
            tempDirectory: args.usesTempDirectory && Context.getTempDirectory(args.interpreter.exe.nameWithoutExtension)
        }).getOutputFiles();
    }

    /**
     * Verify the required arguments are set to valid values.
     */
    function validateArguments(args: Arguments): void {
        Contract.assert(args.interpreter !== undefined, "The interpreter must be defined");
        Contract.assert(args.script !== undefined, "The script must be defined");

        if ((args.implicitOutputs === undefined || args.implicitOutputs.length === 0) &&
            (args.rewrittenFiles === undefined || args.rewrittenFiles.length === 0) &&
            args.standardOutput !== undefined &&
            args.standardError !== undefined) {
            Contract.fail("Cannot operate on an empty file set");
        }
    }
}
