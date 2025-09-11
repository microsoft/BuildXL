// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

import * as LinuxSandboxTest from "BuildXL.Sandbox.Linux.UnitTests";
import * as DetoursTest from "BuildXL.Sandbox.Windows.DetoursTests";
const DetoursTest64 = DetoursTest.withQualifier({platform: "x64"});

namespace Processes {
    
    // BuildXL.Processes is still used as Net472 by MsBuild. So maintain the tests for net472
    export declare const qualifier: BuildXLSdk.DefaultQualifierWithNet472;
    const bxlSdk = importFrom("Sdk.BuildXL");

    const testArgs : BuildXLSdk.TestArguments = {
        assemblyName: "Test.BuildXL.Processes",
        allowUnsafeBlocks: true,
        runTestArgs: {
            // These tests require Detours to run itself, so we won't detour the test runner process itself
            unsafeTestRunArguments: {
                runWithUntrackedDependencies: !BuildXLSdk.Flags.IsEBPFSandboxForTestsEnabled,
                untrackedPaths:[
                    // CODESYNC: Public/Src/Engine/UnitTests/Scheduler/PipTestBase.cs
                    r`TestProcess/Unix/Test.BuildXL.Executables.TestProcessWithCapabilities`
                ],
            },
            // Code coverage utilities can interefe with our sandbox tests and
            // cause failures as they can inject extraneous processes into our sandboxes
            // (e.g., IntelliTrace.exe): this has caused test flakiness in the past (see bug #1908180). 
            disableCodeCoverage: true,
            parallelGroups: ["FileAccessExplicitReportingTest", "DetoursCrossBitnessTest"],
            parallelBucketCount: 16,
        },
        assemblyBindingRedirects: [
            ...BuildXLSdk.bxlBindingRedirects(),
            {
                name: "System.Numerics.Vectors",
                publicKeyToken: "b03f5f7f11d50a3a",
                culture: "neutral",
                oldVersion: "0.0.0.0-4.1.4.0",
                newVersion: "4.1.4.0", // Corresponds to: { id: "System.Numerics.Vectors", version: "4.5.0" },
            },
            {
                name: "System.Text.Json",
                publicKeyToken: "cc7b13ffcd2ddd51",
                culture: "neutral",
                oldVersion: "0.0.0.0-6.0.0.0",
                newVersion: "6.0.0.0",
            },
        ],
        references: [
            EngineTestUtilities.dll,
            Scheduler.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Engine").ProcessPipExecutor.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Engine").Processes.External.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Plugin.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("BuildXL.Utilities.UnitTests").TestProcess.exe,
            ...importFrom("BuildXL.Utilities").Native.securityDlls,
            ...addIf(bxlSdk.isFullFramework,
                bxlSdk.NetFx.System.IO.Compression.dll,
                bxlSdk.NetFx.System.Management.dll,
                bxlSdk.NetFx.System.Net.Http.dll,
                bxlSdk.NetFx.Netstandard.dll
            ),
            ...addIfLazy(bxlSdk.Flags.isMicrosoftInternal, () => [
                  importFrom("AnyBuild.SDK").pkg,
            ])
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
                                Processes.TestPrograms.RemoteApi.withQualifier({platform: "x64"}).exe.binaryFile,
                            ],
                        }
                    ]
                },
            ]),
            importFrom("BuildXL.Utilities.UnitTests").TestProcess.deploymentDefinition,
            importFrom("BuildXL.Utilities.UnitTests").InfiniteWaiter.exe,
            ...addIfLazy(qualifier.targetRuntime === "linux-x64", () => [
                {
                    subfolder: "LinuxTestProcesses",
                    contents: [
                        LinuxSandboxTest.StaticLinkingTestProcess.exe(true),
                        LinuxSandboxTest.StaticLinkingTestProcess.exe(false),
                        ...LinuxSandboxTest.UnitTests.BoostTestExecutables,
                        LinuxSandboxTest.LinuxTestProcess.exe(),
                    ]
                }
            ]),
            ...addIfLazy(qualifier.targetRuntime === "linux-x64", () => [
                {
                    subfolder: a`RingBufferTest`,
                    contents: [
                        importFrom("BuildXL.Sandbox.Linux.UnitTests").Test.eBPFSandbox.ringbufferTest,
                        importFrom("BuildXL.Sandbox.Linux.UnitTests").Test.eBPFSandbox.sendProbe,
                        importFrom("BuildXL.Sandbox.Linux.UnitTests").Test.eBPFSandbox.pathCanonicalization,
                    ]
                }
            ])
        ]
    };

        @@public
    export const test_BuildXL_Processes_dll = BuildXLSdk.test(Object.merge<BuildXLSdk.TestArguments>(testArgs, {
        // We exclude PTraceSandboxedProcessTest.cs altogether when EBPFSandboxForTests is enabled.
        // We don't have a test attribute today to control this, so we filter the sources directly.
        // TODO: consider adding one, or evaluate how far we are from retiring interpose
        sources: globR(d`.`, "*.cs").filter(
            (file) => !importFrom("Sdk.BuildXL").Flags.IsEBPFSandboxForTestsEnabled || file.name !== a`PTraceSandboxedProcessTest.cs`)}));
}
