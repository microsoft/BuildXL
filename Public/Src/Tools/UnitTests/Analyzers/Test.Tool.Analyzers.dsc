// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as Deployment from "Sdk.Deployment";

namespace Test.Tool.Analyzers {
    export declare const qualifier : BuildXLSdk.DefaultQualifier;

    @@public
    export const dll = BuildXLSdk.test({
        // These tests require Detours to run itself, so we won't detour the test runner process itself
        runTestArgs: {
            unsafeTestRunArguments: {
                runWithUntrackedDependencies: !BuildXLSdk.Flags.IsEBPFSandboxForTestsEnabled,
                untrackedScopes: [
                    // Access to the cryptography store
                    ...addIfLazy(Context.getCurrentHost().os !== "win", () => [d`${Environment.getDirectoryValue("HOME")}/.dotnet`])
                ]
            },
        },
        assemblyName: "Test.Tool.Analyzers",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Engine").Cache.dll,
            importFrom("BuildXL.Engine").Engine.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.FrontEnd").Script.dll,
            importFrom("BuildXL.Ide").Script.Debugger.dll,
            importFrom("BuildXL.Ide").VSCode.DebugProtocol.dll,
            importFrom("BuildXL.Core.UnitTests").EngineTestUtilities.dll,
            importFrom("BuildXL.Core.UnitTests").Scheduler.IntegrationTest.dll,
            importFrom("BuildXL.Core.UnitTests").Scheduler.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Tools").Execution.Analyzer.exe,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("BuildXL.Utilities.UnitTests").TestProcess.exe,
            importFrom("BuildXL.Utilities.UnitTests").StorageTestUtilities.dll,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("ZstdSharp.Port").pkg,
        ],
        runtimeContent: [
            importFrom("BuildXL.Utilities.UnitTests").testProcessExe
        ]
    });
}
