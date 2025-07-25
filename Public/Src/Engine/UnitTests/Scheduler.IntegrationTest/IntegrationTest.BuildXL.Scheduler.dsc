// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as Deployment from "Sdk.Deployment";
import * as LinuxSandboxTestProcess from "BuildXL.Sandbox.Linux.UnitTests";

namespace Scheduler.IntegrationTest {
    export declare const qualifier : BuildXLSdk.DefaultQualifier;

    const includeCredScan = BuildXLSdk.Flags.isMicrosoftInternal && BuildXLSdk.isDotNetCore;

    const testArgs : BuildXLSdk.TestArguments = {
        assemblyName: "IntegrationTest.BuildXL.Scheduler",
        sources: globR(d`.`, "*.cs"),
        runTestArgs: {
            unsafeTestRunArguments: {
                // These tests require Detours to run itself, so we won't detour the test runner process itself
                runWithUntrackedDependencies: !BuildXLSdk.Flags.IsEBPFSandboxForTestsEnabled,
                untrackedScopes: [
                    // Access to the cryptography store
                    ...addIfLazy(!Context.isWindowsOS(), () => [d`${Environment.getDirectoryValue("HOME")}/.dotnet`])
                ],
                untrackedPaths:[
                    // CODESYNC: Public/Src/Engine/UnitTests/Scheduler/PipTestBase.cs
                    r`TestProcess/Unix/Test.BuildXL.Executables.TestProcessAlternative`
                ],
            },
            parallelBucketCount: 30
        },
        references: [
            Scheduler.dll,
            EngineTestUtilities.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Engine").Cache.dll,
            importFrom("BuildXL.App").Main.exe,
            importFrom("BuildXL.Engine").Engine.dll,
            importFrom("BuildXL.Engine").ProcessPipExecutor.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Engine").Processes.External.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.Engine").ViewModel.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("BuildXL.Utilities.UnitTests").TestProcess.exe,
            importFrom("BuildXL.Utilities.UnitTests").StorageTestUtilities.dll,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            ...addIf(includeCredScan,
                importFrom("Microsoft.Security.Utilities.Internal").pkg
            ),
        ],
        runtimeContent: [
            importFrom("BuildXL.Utilities.UnitTests").TestProcess.deploymentDefinition,
            ...addIfLazy(qualifier.targetRuntime === "linux-x64", () => [
                {
                    subfolder: "LinuxTestProcesses",
                    contents: [
                        LinuxSandboxTestProcess.StaticLinkingTestProcess.exe(true),
                        LinuxSandboxTestProcess.StaticLinkingTestProcess.exe(false),
                    ]
                }
            ]),
        ],
    };

    @@public
    export const dll = BuildXLSdk.test(testArgs);
}
