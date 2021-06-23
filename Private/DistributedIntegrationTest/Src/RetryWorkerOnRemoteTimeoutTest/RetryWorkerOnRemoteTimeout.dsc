// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import {cmdExe, verifyOutputScript} from "DistributedIntegrationTests.Utils";

function main() {
    const outputDir1 = Context.getNewOutputDirectory("outputDirectory1");
    const outputFile1 = p`${outputDir1}/outputfile1.txt`;
    
    let pipA = Transformer.execute({
        tool: cmdExe,
        description: "First process pip in RetryWorkerOnRemoteTimeout",
        arguments: [
            Cmd.argument("/d"),
            Cmd.argument("/c"),
            Cmd.argument("echo Test"),
            Cmd.argument(">"),
            Cmd.argument(Artifact.output(outputFile1)),
        ],
        workingDirectory: d`.`
    });

    const outputFile2 = p`${outputDir1}/outputfile2.txt`;

    let pipB = Transformer.execute({
        tool: cmdExe,
        description: "2nd process pip causing a timeout and retry on another worker",
        // Tags will cause this pip to run on a remotr worker and the engine will simulate a timeout
        tags: ["buildxl.internal:runRemotely", "buildxl.internal:triggerWorkerRemotePipTimeout"],        
        arguments: [
            Cmd.argument("/d"),
            Cmd.argument("/c"),
            Cmd.argument("type"),
            Cmd.argument(Artifact.input(pipA.getOutputFile(outputFile1))),
            Cmd.argument(">"),
            Cmd.argument(Artifact.output(outputFile2)),
        ],
        workingDirectory: d`.`,
        priority: 99, // indicate that this is used for integration test purposes so the tags above apply.
    });

    const outputFile3 = p`${outputDir1}/outputfile3.txt`;

    // outputDirectory1/outputfile1.txt -> Process2 -> outputDirectory1/outputfile2.txt
    let result = Transformer.execute({
        tool: cmdExe,
        description: "Third process pip in RetryWorkerOnRemoteTimeout",
        arguments: [
            Cmd.argument("/d"),
            Cmd.argument("/c"),
            Cmd.argument("type"),
            Cmd.argument(Artifact.input(pipB.getOutputFile(outputFile2))),
            Cmd.argument(">"),
            Cmd.argument(Artifact.output(outputFile3)),
        ],
        workingDirectory: d`.`
    });

    
    return 0;
}

@@public
export const go = main();