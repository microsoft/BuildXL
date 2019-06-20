// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

import * as DetoursTest from "BuildXL.Sandbox.Windows.DetoursTests";
const DetoursTest64 = DetoursTest.withQualifier({platform: "x64", configuration: qualifier.configuration});

namespace Processes {
    @@public
    export const test_BuildXL_Processes_dll = BuildXLSdk.test({
        // These tests require Detours to run itself, so we can't detour xunit itself
        // TODO: QTest
        testFramework: importFrom("Sdk.Managed.Testing.XUnit.UnsafeUnDetoured").framework,

        assemblyName: "Test.BuildXL.Processes",
        allowUnsafeBlocks: true,
        sources: globR(d`.`, "*.cs"),
        runTestArgs: {
            parallelGroups: ["FileAccessExplicitReportingTest", "DetoursCrossBitnessTest"]
        },
        references: [
            EngineTestUtilities.dll,
            Scheduler.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Interop.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
            importFrom("BuildXL.Utilities.UnitTests").TestProcess.exe,
            ...importFrom("BuildXL.Utilities").Native.securityDlls
        ],
        runtimeContent: [
            ...addIfLazy(qualifier.targetRuntime === "win-x64", () => [
                // Note that detoursservice is deployed both in root and in the detourscrossbit tests to handle dotnetcore tests not being fully netstandard2.0
                {
                    subfolder: a`DetoursCrossBitTests`,
                    contents: [
                        DetoursCrossBitTests.x64,
                        DetoursCrossBitTests.x86,
                        {
                            subfolder: a`x64`,
                            contents: [
                                DetoursTest64.inputFile,
                                DetoursTest64.exe.binaryFile,
                                Processes.TestPrograms.RemoteApi.withQualifier({configuration: qualifier.configuration, platform: "x64"}).exe.binaryFile,
                            ],
                        }
                    ]
                },
                importFrom("BuildXL.Utilities.UnitTests").TestProcess.deploymentDefinition
            ]),
        ],
    });
}
