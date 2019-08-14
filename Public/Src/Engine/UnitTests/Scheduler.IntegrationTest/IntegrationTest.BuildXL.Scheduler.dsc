// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as Deployment from "Sdk.Deployment";

namespace Scheduler.IntegrationTest {

    @@public
    export const categoriesToRunInParallel =  [
        "BaselineTests",
        "FileAccessPolicyTests",
        "OpaqueDirectoryTests",
        "SharedOpaqueDirectoryTests",
        "AllowedUndeclaredReadsTests",
        "LazyMaterializationTests",
        "WhitelistTests",
        "PreserveOutputsTests",
        "NonStandardOptionsTests",
        "StoreNoOutputsToCacheTests",
        // "IncrementalSchedulingTests", TODO: Some shared tests (IS vs. non-IS) create substs, and this can cause race.
    ];

    @@public
    export const dll = BuildXLSdk.test({
        // These tests require Detours to run itself, so we can't detour xunit itself
        // TODO: QTest
        testFramework: importFrom("Sdk.Managed.Testing.XUnit.UnsafeUnDetoured").framework,

        assemblyName: "IntegrationTest.BuildXL.Scheduler",
        sources: globR(d`.`, "*.cs"),
        runTestArgs: {
            parallelGroups: categoriesToRunInParallel
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
