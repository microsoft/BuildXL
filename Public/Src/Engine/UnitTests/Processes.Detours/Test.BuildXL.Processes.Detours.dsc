// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as DetoursTest from "BuildXL.Sandbox.Windows.DetoursTests";
const DetoursTest64 = DetoursTest.withQualifier({platform: "x64", configuration: qualifier.configuration});
const DetoursTest86 = DetoursTest.withQualifier({platform: "x86", configuration: qualifier.configuration});
import * as Managed from "Sdk.Managed";

namespace Processes.Detours {
    function test(platform: string) {

        const detours = platform === "x64" ? DetoursTest64 : DetoursTest86;
        const assemblyName = "Test.BuildXL.Processes.Detours";

        return BuildXLSdk.test({
            // These tests require Detours to run itself, so we can't detour xunit itself
            // TODO: QTest
            testFramework: importFrom("Sdk.Managed.Testing.XUnit.UnsafeUnDetoured").framework,

            assemblyName: assemblyName,
            standaloneTestFolder: a`${assemblyName}.${platform}`,

            sources: [
                f`PipExecutorDetoursTest.cs`,
                f`SandboxedProcessPipExecutorTest.cs`,
                f`SandboxedProcessPipExecutorWindowsCallTest.cs`,
                f`ValidationDataCreator.cs`,
                f`FileAccessManifestTreeTest.cs`,
                f`SandboxedProcessInfoTest.cs`,
                f`SubstituteProcessExecutionTests.cs`,
            ],
            references: [
                EngineTestUtilities.dll,
                importFrom("BuildXL.Engine").Processes.dll,
                importFrom("BuildXL.Pips").dll,
                importFrom("BuildXL.Utilities").dll,
                importFrom("BuildXL.Utilities").Native.dll,
                importFrom("BuildXL.Utilities").Storage.dll,
                importFrom("BuildXL.Utilities").Collections.dll,
                importFrom("BuildXL.Utilities").Configuration.dll,
                importFrom("BuildXL.Utilities.UnitTests").Storage.dll,
                Processes.test_BuildXL_Processes_dll,
            ],
            runtimeContent: [
                TestSubstituteProcessExecutionShim.exe,
                ...addIfLazy(qualifier.targetRuntime === "win-x64", () => [
                    {
                        subfolder: a`${platform}`,
                        contents: [
                            detours.inputFile,
                            detours.exe.binaryFile
                        ]
                    }
                ])
            ],
            defineConstants: platform === "x64"
                ? ["TEST_PLATFORM_X64"]
                : ["TEST_PLATFORM_X86"]
        });
    }

    @@public
    export const test_Processes_Detours_x64_dll = test("x64");

    @@public
    export const test_Processes_Detours_x86_dll = test("x86");
}
