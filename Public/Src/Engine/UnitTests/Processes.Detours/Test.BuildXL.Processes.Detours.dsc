// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as DetoursTest from "BuildXL.Sandbox.Windows.DetoursTests";
import * as Managed from "Sdk.Managed";
import * as XUnit from "Sdk.Managed.Testing.XUnit";

const DetoursTest64 = DetoursTest.withQualifier({platform: "x64"});
const DetoursTest86 = DetoursTest.withQualifier({platform: "x86"});
const SubstitutePlugin64 = Processes.TestPrograms.SubstituteProcessExecutionPlugin.withQualifier({platform: "x64"});
const SubstitutePlugin86 = Processes.TestPrograms.SubstituteProcessExecutionPlugin.withQualifier({platform: "x86"});

namespace Processes.Detours {
    function test(platform: string) {
        const detours = platform === "x64" ? DetoursTest64 : DetoursTest86;
        const assemblyName = "Test.BuildXL.Processes.Detours";

        return BuildXLSdk.test({
            runTestArgs: {
                // These tests require Detours to run itself, so we won't detour the test runner process itself
                unsafeTestRunArguments: {
                    runWithUntrackedDependencies: true
                },
            },
            // Use XUnit because the unit tests create junction and directory symlinks. 
            // QTest runs Robocopy on the temporary directory where the tests create those junctions and directory symlinks.
            // Unfortunately, Robocopy does not know how to copy directory symlinks or junctions.
            testFramework: XUnit.framework,
            assemblyName: assemblyName,
            sources: [
                f`PipExecutorDetoursTest.cs`,
                f`SandboxedProcessPipExecutorTest.cs`,
                f`SandboxedProcessPipExecutorWindowsCallTest.cs`,
                f`ValidationDataCreator.cs`,
                f`FileAccessManifestTreeTest.cs`,
                f`SandboxedProcessInfoTest.cs`,
                f`SubstituteProcessExecutionTests.cs`,
                f`OutputFilterTest.cs`,
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
                    },
                    {
                        subfolder: r`SubstitutePlugin/x64`,
                        contents: [ SubstitutePlugin64.dll.binaryFile ]
                    },
                    {
                        subfolder: r`SubstitutePlugin/x86`,
                        contents: [ SubstitutePlugin86.dll.binaryFile ]
                    }
                ]),
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
