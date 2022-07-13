// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

namespace Scheduler {

    // BuildXL.Processes is still used as Net472 by Cloudbuild and the unittest for that relies on this test dll.
    // Therefore we compile, but don't run the test in net472
    export declare const qualifier: BuildXLSdk.DefaultQualifierWithNet472;

    @@public
    export const categoriesToRunInParallel = [
        "OperationTrackerTests",
        "FileContentManagerTests",
        "SchedulerTest",
        "PipExecutorTest",
        "ExecutionLogTests",
    ];

    @@public
    export const dll = BuildXLSdk.test({
        // These tests require Detours to run itself, so we won't detour the test runner process itself
        assemblyName: "Test.BuildXL.Scheduler",
        sources: globR(d`.`, "*.cs"),
        runTestArgs: {
            unsafeTestRunArguments: {
                runWithUntrackedDependencies: true
            },
            parallelGroups: categoriesToRunInParallel,
        },
        skipTestRun: qualifier.targetFramework === "net472" || !BuildXLSdk.targetFrameworkMatchesCurrentHost,
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                BuildXLSdk.NetFx.System.Reflection.dll
            ),
            EngineTestUtilities.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Engine").Cache.dll,
            importFrom("BuildXL.Engine").Engine.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").Ipc.Providers.dll,
            importFrom("BuildXL.Utilities").Interop.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Plugin.dll,
            importFrom("BuildXL.Utilities.UnitTests").StorageTestUtilities.dll,
            importFrom("BuildXL.Utilities.UnitTests").TestProcess.exe,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("BuildXL.Utilities").Configuration.dll,
        ],
        runtimeContent: [
            importFrom("BuildXL.Utilities.UnitTests").testProcessExe
        ],
    });
}
