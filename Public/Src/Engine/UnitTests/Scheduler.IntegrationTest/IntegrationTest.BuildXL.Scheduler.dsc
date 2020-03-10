// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as Deployment from "Sdk.Deployment";

namespace Scheduler.IntegrationTest {
    export declare const qualifier : BuildXLSdk.DefaultQualifier;
    
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "IntegrationTest.BuildXL.Scheduler",
        sources: globR(d`.`, "*.cs"),
        runTestArgs: {
            unsafeTestRunArguments: {
                // These tests require Detours to run itself, so we won't detour the test runner process itself
                runWithUntrackedDependencies: true
            },
            parallelGroups: [
                "AllowedUndeclaredReadsTests",
                "BaselineTests",
                "FileAccessPolicyTests",
                "IncrementalSchedulingTests",
                "LazyMaterializationTests",
                "NonStandardOptionsTests",
                "OpaqueDirectoryTests",
                "PreserveOutputsTests",
                "PreserveOutputsReuseOutputsTests",
                "PreserveOutputsReuseIncSchedTests",
                "PreserveOutputsIncSchedTests",
                "SharedOpaqueDirectoryTests",
                "StoreNoOutputsToCacheTests",
                "WhitelistTests"
            ]
        },
        references: [
            Scheduler.dll,
            EngineTestUtilities.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Engine").Cache.dll,
            importFrom("BuildXL.App").Main.exe,
            importFrom("BuildXL.Engine").Engine.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.Engine").ViewModel.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Interop.dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities.UnitTests").TestProcess.exe,
            importFrom("BuildXL.Utilities.UnitTests").StorageTestUtilities.dll,
            importFrom("Newtonsoft.Json").pkg,
        ],
        runtimeContent: [
            importFrom("BuildXL.Utilities.UnitTests").TestProcess.deploymentDefinition,
        ],
    });
}
