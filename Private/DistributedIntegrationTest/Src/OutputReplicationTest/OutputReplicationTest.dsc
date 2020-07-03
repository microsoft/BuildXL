// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import {cmdExe} from "DistributedIntegrationTests.Utils";

function main() {
    const outputDir = Context.getNewOutputDirectory("outputDir");
    const outputA = p`${outputDir}/${Environment.getStringValue("OUTPUT_FILENAME_FOR_REPLICATION")}`;
    
    const pipA = Transformer.execute({
        tool: cmdExe,
        arguments: [
            Cmd.argument("/d"),
            Cmd.argument("/c"),
            Cmd.argument("echo"),
            Cmd.argument(Environment.getFlag("[Test]FailOutputReplicationTest") ? "FAIL" : "SUCCESS"),
            Cmd.argument(">"),
            Cmd.argument(Artifact.output(outputA)),
        ],
        workingDirectory: d`.`
    });

    const outputB = p`${outputDir}/outputB.txt`;
    const pipB = Transformer.execute({
        tool: cmdExe,
        arguments: [
            Cmd.argument("/d"),
            Cmd.argument("/c"),
            Cmd.argument(Artifact.input(f`./copyOrFail.cmd`)),
            Cmd.argument(Artifact.input(pipA.getOutputFile(outputA))),
            Cmd.argument(Artifact.output(outputB)),
        ],
        workingDirectory: d`.`,
        environmentVariables: [
            { name: "FAILME", value: Environment.getFlag("[Test]FailOutputReplicationTest") ? "1" : "0" }
        ]
    });

    const outputC = p`${outputDir}/outputC.txt`;
    const pipC = Transformer.execute({
        tool: cmdExe,
        arguments: [
            Cmd.argument("/d"),
            Cmd.argument("/c"),
            Cmd.argument("type"),
            Cmd.argument(Artifact.input(pipB.getOutputFile(outputB))),
            Cmd.argument(">"),
            Cmd.argument(Artifact.output(outputC)),
        ],
        workingDirectory: d`.`
    });
}

@@public
export const go = main();