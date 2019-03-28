// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Drop from "Sdk.Drop"; 
import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import {range, cmdExe} from "DistributedIntegrationTests.Utils";

type DropOperationArguments = Drop.DropOperationArguments; 

const numServices = 2;
const numPingRequestsPerService = 100;
const numReadfileRequestsPerService = 100;

const runner = Drop.runner;

@@public
export const test = 
    range(0, numServices)
        .map(port => doTest(port, numPingRequestsPerService, numReadfileRequestsPerService));

function doTest(port: number, numPings: number, numReadfiles: number) {
    const serviceStart = runner.startDaemonNoDrop({verbose: true});
    const daemonArgs = <DropOperationArguments>{
        maxConnectRetries: 4,
        connectRetryDelayMillis: 500,
        verbose: true
    };

    // run pings
    range(1, numPings).map(idx =>
        runner.pingDaemon(serviceStart, daemonArgs)
    );

    // run readfiles
    const outDir = Context.getNewOutputDirectory("dropd-test-read");
    range(1, numReadfiles).map(idx => {
        const file = f`test-file.txt`;
        const readResult = runner.testReadFile(serviceStart, file, daemonArgs);
        assertSameFileContents(file, readResult.outputs[0]);
    });
}

function assertSameFileContents(f1: File, f2: File) {
    Transformer.execute({
        tool: cmdExe,
        arguments: [
            Cmd.argument("/d"),
            Cmd.argument("/c"),
            Cmd.argument("fc"),
            Cmd.argument(Artifact.input(f1)),
            Cmd.argument(Artifact.input(f2)),
        ],
        consoleOutput: Context.getNewOutputDirectory("fc").combine("fc-stdout.txt"),
        workingDirectory: d`.`
    });
}
