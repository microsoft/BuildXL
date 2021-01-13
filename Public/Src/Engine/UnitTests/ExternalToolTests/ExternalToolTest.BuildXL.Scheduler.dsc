// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as Deployment from "Sdk.Deployment";
import * as XUnit from "Sdk.Managed.Testing.XUnit";

namespace ExternalToolTest {
    export declare const qualifier : BuildXLSdk.DefaultQualifier;

    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "ExternalToolTest.BuildXL.Scheduler",
        runTestArgs: {
            unsafeTestRunArguments: {
                // These tests require Detours to run itself, so we won't detour the test runner process itself
                runWithUntrackedDependencies: true
            },
        },
        testFramework: XUnit.framework,
        sources: globR(d`.`, "*.cs"),
        references: [
            Scheduler.dll,
            EngineTestUtilities.dll,
            Scheduler.IntegrationTest.dll,
            importFrom("BuildXL.Engine").Engine.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities.UnitTests").TestProcess.exe,
        ],
        runtimeContent: [
            importFrom("BuildXL.Utilities.UnitTests").TestProcess.deploymentDefinition,
            importFrom("BuildXL.Tools").SandboxedProcessExecutor.exe,
            // TODO: Move it to the root when we can access the real VmCommandProxy in CB.
            {
                subfolder: r`tools/VmCommandProxy/tools`,
                contents: [
                    importFrom("BuildXL.Utilities.UnitTests").MockVmCommandProxy.exe
                ]
            }
        ],
    });
}
