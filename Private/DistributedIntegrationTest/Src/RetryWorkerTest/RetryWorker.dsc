// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import {cmdExe, verifyOutputScript} from "DistributedIntegrationTests.Utils";

function main() {
    const outputDir1 = Context.getNewOutputDirectory("outputDirectory1");
    const outputFile1 = p`${outputDir1}/outputfile1.txt`;
    
    let result = Transformer.execute({
        tool: cmdExe,
        description: "Process pip causing a retry on another worker",
        tags: ["buildxl.internal:triggerWorkerConnectionTimeout"],         // Tag specified in TagFilter.TriggerWorkerConnection and causes this pip to run on a worker rather than running on master
        arguments: [
            Cmd.argument("/d"),
            Cmd.argument("/c"),
            Cmd.argument("echo Test"),
            Cmd.argument(">"),
            Cmd.argument(Artifact.output(outputFile1)),
        ],
		priority: 99,
        workingDirectory: d`.`
    });

    return result;
}

@@public
export const go = main();