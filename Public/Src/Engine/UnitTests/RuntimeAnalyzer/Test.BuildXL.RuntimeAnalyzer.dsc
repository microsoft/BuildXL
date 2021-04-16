// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as Deployment from "Sdk.Deployment";

namespace Test.BuildXL.RuntimeAnalyzer {
    export declare const qualifier : BuildXLSdk.DefaultQualifier;

    @@public
    export const dll = BuildXLSdk.test({
        runTestArgs: {
            unsafeTestRunArguments: {
                runWithUntrackedDependencies: true
            },
        },
        assemblyName: "Test.BuildXL.RuntimeAnalyzer",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Engine").Engine.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            Scheduler.IntegrationTest.dll,
            Scheduler.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities.UnitTests").TestProcess.exe
        ],
        runtimeContent: [
            importFrom("BuildXL.Utilities.UnitTests").TestProcess.deploymentDefinition
        ]
    });
}