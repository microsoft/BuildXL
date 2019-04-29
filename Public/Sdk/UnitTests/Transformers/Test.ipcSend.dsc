// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Assert, Testing} from "Sdk.Testing";
import {Transformer} from "Sdk.Transformers";

namespace Sdk.Tests {
    @@Testing.unitTest()
    export function fullIpcSend() {
        const moniker = Transformer.getNewIpcMoniker();

        const shutdownCmd = <Transformer.ExecuteArguments>{
            tool: { exe: f`src/tool/tool.exe` },
            arguments: [],
            workingDirectory: d`out/working-shutdown`,
            consoleOutput: p`out/shutdown-stdout.txt`
        };

        const finalizationCmd = <Transformer.IpcSendArguments>{
            moniker: moniker,
            connectRetryDelayMillis: 1000,
            maxConnectRetries: 2,
            fileDependencies: [],
            lazilyMaterializedDependencies: [],
            messageBody: [],
            outputFile: p`out/stdout-finalization.txt`,
            mustRunOnMaster: true,
            targetService: undefined
        };

        const servicePip = Transformer.createService({
            tool: { exe: f`src/tool/tool.exe` },
            arguments: [],
            workingDirectory: d`out/working-service`,
            consoleOutput: p`out/service-stdout.txt`,
            serviceShutdownCmd: shutdownCmd,
            serviceFinalizationCmds: [ finalizationCmd ]
        });

        const staticDirectory = Transformer.sealDirectory({root: d`src/dir`, files: [f`src/dir/file.txt`]});

        Transformer.ipcSend({
            moniker: moniker,
            connectRetryDelayMillis: 1000,
            maxConnectRetries: 2,
            fileDependencies: [ f`src/ipc-src.txt` ],
            lazilyMaterializedDependencies: [ f`src/ipc-src.txt`, staticDirectory ],
            messageBody: [],
            outputFile: p`out/stdout1.txt`,
            targetService: servicePip.serviceId,
            mustRunOnMaster: true
        });

        Transformer.ipcSend({
            moniker: moniker,
            connectRetryDelayMillis: 1000,
            maxConnectRetries: 2,
            fileDependencies: [],
            lazilyMaterializedDependencies: [],
            messageBody: [],
            outputFile: p`out/stdout2.txt`,
            targetService: servicePip.serviceId,
            mustRunOnMaster: false
        });
    }
}
