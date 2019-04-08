// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

/**
 * This test is concerned with testing (opaque) output directories.
 * This test creates N pips, each producing an output directory, and
 * N/2 pips, each consuming one or two output directories produced by the former pips.
 * The value of N can be controlled by %OUTPUT_DIRECTORY_TEST_NUM_OF_PRODUCERS%, or
 * by default, the value of N is 10.
 *
 * The producer invokes "produceOutputDirectory.cmd" to create an output directory,
 * while the consumer invokes "enumerateDirectory.cmd" or "enumerateTwoDirectories.cmd"
 * to consume and validate the directories that were produced by the producer.
 */

import {Artifact, Cmd} from "Sdk.Transformers";
import {cmdExe} from "DistributedIntegrationTests.Utils";

const defaultNumOfProducers = 10;

function produceDirectories(numOfProducers: number, createSharedOpaques : boolean): StaticDirectory[] {
    const dirName = createSharedOpaques
        ? "producedDirectories"
        : "producedDirectoriesSharedOpaques";
    const outputDirectory = Context.getNewOutputDirectory(dirName);
    let directories = [];

    for (let i = 0; i < numOfProducers; i++) {
        directories = directories.push(produceDirectory(i, outputDirectory, createSharedOpaques));
    }

    return directories;
}

function produceDirectory(index: number, outputDirectory: Directory, createSharedOpaques : boolean): StaticDirectory {
    const indexedOutputDirectory = d`${outputDirectory}/${index.toString()}`;
    const executeResult = Transformer.execute({
        tool: cmdExe,
        arguments: [
            Cmd.argument("/d"),
            Cmd.argument("/c"),
            Cmd.argument(Artifact.input(f`./produceOutputDirectory.cmd`)),
            createSharedOpaques
                ? Cmd.argument(Artifact.sharedOpaqueOutput(indexedOutputDirectory))
                : Cmd.argument(Artifact.output(indexedOutputDirectory))
        ],
        workingDirectory: d`.`
    });

    return executeResult.getOutputDirectory(indexedOutputDirectory);
}

function consumeDirectories(directories: StaticDirectory[], consumeSharedOpaques : boolean) {
    const dirName = consumeSharedOpaques
        ? "consumedDirectories"
        : "consumedDirectoriesSharedOpaques";
    const outputDirectory = Context.getNewOutputDirectory(dirName);
    for (let i = 0; i < directories.length; i++) {
        if (i < directories.length - 1) {
            consumeTwoDirectories(i, directories[i], i + 1, directories[i + 1], outputDirectory);
            i = i + 1;
        } else {
            consumeDirectory(i, directories[i], outputDirectory);
        }
    }
}

function consumeTwoDirectories(index1: number, directory1: StaticDirectory, index2: number, directory2: StaticDirectory, outputDirectory: Directory) {
    const indexes = `${index1.toString()}_${index2.toString()}`;
    const indexedOutputFile = p`${outputDirectory}/${indexes}/result.txt`;
    const executeResult = Transformer.execute({
        tool: cmdExe,
        arguments: [
            Cmd.argument("/d"),
            Cmd.argument("/c"),
            Cmd.argument(Artifact.input(f`./enumerateTwoDirectories.cmd`)),
            Cmd.argument(Artifact.input(directory1)),
            Cmd.argument(Artifact.input(directory2)),
            Cmd.argument(Artifact.output(indexedOutputFile)),
            Cmd.argument(Artifact.input(f`./expectedOut.txt`))
        ],
        workingDirectory: d`.`
    });
}

function consumeDirectory(index: number, directory: StaticDirectory, outputDirectory: Directory) {
    const indexedOutputFile = p`${outputDirectory}/${index.toString()}/result.txt`;
    const executeResult = Transformer.execute({
        tool: cmdExe,
        arguments: [
            Cmd.argument("/d"),
            Cmd.argument("/c"),
            Cmd.argument(Artifact.input(f`./enumerateDirectory.cmd`)),
            Cmd.argument(Artifact.input(directory)),
            Cmd.argument(Artifact.output(indexedOutputFile)),
            Cmd.argument(Artifact.input(f`./expectedOut.txt`))
        ],
        workingDirectory: d`.`
    });
}

function main() {
    const numOfProducers = Environment.hasVariable("OUTPUT_DIRECTORY_TEST_NUM_OF_PRODUCERS") 
        ? Environment.getNumberValue("OUTPUT_DIRECTORY_TEST_NUM_OF_PRODUCERS") 
        : defaultNumOfProducers;
    const exclusiveOpaqueDirectories = produceDirectories(numOfProducers, false);
    consumeDirectories(exclusiveOpaqueDirectories, false);

    const sharedOpaquesDirectories = produceDirectories(numOfProducers, true);
    consumeDirectories(sharedOpaquesDirectories, true);
    return 0;
}

@@public
export const go = main();
