// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/**
 * This test is concerned with testing seal directory pips, particularly seal source ones, in distributed builds.
 * This test creates N pips that consume directories that are sealed using source seal directory pip and standard seal directory pip.
 * The value of N can be controlled by %SEAL_DIRECTORY_TEST_NUM_OF_CONSUMERS%, or
 * by default, the value of N is 10.
 */

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import {cmdExe} from "DistributedIntegrationTests.Utils";

const defaultNumOfConsumers = 10;

function consumeDirectories(numOfConsumers: number): DerivedFile[] {
    const outputDirectory = Context.getNewOutputDirectory("consumeDirectories");
    let files = [];

    for (let i = 0; i < numOfConsumers; i++) {
        files = files.push(consumeDirectory(i, outputDirectory));
    }

    return files;
}

function consumeDirectory(index: number, outputDirectory: Directory): DerivedFile {
    const indexedOutputFile = p`${outputDirectory}/${index.toString()}/output.txt`;
    const sealedSource = Transformer.sealSourceDirectory(d`./sealedSourceDirectory`, Transformer.SealSourceDirectoryOption.allDirectories);
    const sealedPartial = Transformer.sealPartialDirectory(d`./sealedDirectory`, globR(d`./sealedDirectory`));
    const executeResult = Transformer.execute({
        tool: cmdExe,
        arguments: [
            Cmd.argument("/d"),
            Cmd.argument("/c"),
            Cmd.argument(Artifact.input(f`./enumerateTwoDirectories.cmd`)),
            Cmd.argument(Artifact.input(sealedSource)),
            Cmd.argument(Artifact.input(sealedPartial)),
            Cmd.argument(Artifact.output(indexedOutputFile))
        ],
        workingDirectory: d`.`
    });

    return executeResult.getOutputFile(indexedOutputFile);
}

function verifyOutputs(files: File[]): DerivedFile[] {
    const outputDirectory = Context.getNewOutputDirectory("verifyOutputs");
    return files.map((f, i) => verifyOutput(f, i, outputDirectory));
}

function verifyOutput(file: File, index: number, outputDirectory: Directory): DerivedFile {
    const indexedOutputFile = p`${outputDirectory}/${index.toString()}/verify.txt`;
    const executeResult = Transformer.execute({
        tool: cmdExe,
        arguments: [
            Cmd.argument("/d"),
            Cmd.argument("/c"),
            Cmd.argument(Artifact.input(f`./verifyOutput.cmd`)),
            Cmd.argument(Artifact.input(f`./expectedOut.txt`)),
            Cmd.argument(Artifact.input(file)),
            Cmd.argument(Artifact.output(indexedOutputFile))
        ],
        workingDirectory: d`.`
    });

    return executeResult.getOutputFile(indexedOutputFile);
}

function main() {
    const numOfConsumers = Environment.hasVariable("SEAL_DIRECTORY_TEST_NUM_OF_CONSUMERS") 
        ? Environment.getNumberValue("SEAL_DIRECTORY_TEST_NUM_OF_CONSUMERS") 
        : defaultNumOfConsumers;
    const files = consumeDirectories(numOfConsumers);
    verifyOutputs(files);
    return 0;
}

@@public
export const go = main();
