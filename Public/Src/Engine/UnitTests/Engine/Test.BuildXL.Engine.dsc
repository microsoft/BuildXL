// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as DetoursServices from "BuildXL.Sandbox.Windows";

namespace Engine {

    const sdkRoot = Context.getMount("SdkRoot").path;

    const libsUsedForTesting = [
        {
            subfolder: r`Sdk/Prelude`,
            contents: glob(d`${sdkRoot}/Prelude`, "*.dsc"),
        },
        {
            subfolder: r`Sdk/Transformers`,
            contents: glob(d`${sdkRoot}/Transformers`, "*.dsc"),
        },
        {
            subfolder: r`Sdk/Deployment`,
            contents: glob(d`${sdkRoot}/Deployment`, "*.dsc"),
        },
    ];

    const microsoftNetCompilerSpec = f`${Context.getMount("FrontEnd").path}/Nuget/specs/Microsoft.Net.Compilers/3.0.0/module.config.bm`;

    @@public
    export const categoriesToRunInParallel = [
        "ValuePipTests",
        "MiniBuildTester",
        "LazyMaterializationBuildTests",
        "DeterminismProbeTests",
        "DirectoryArtifactIncrementalBuildTests"
    ];

    @@public
    export const dll = BuildXLSdk.test({
        // These tests require Detours to run itself, so we can't detour xunit itself
        // TODO: QTest
        testFramework: importFrom("Sdk.Managed.Testing.XUnit.UnsafeUnDetoured").framework,

        assemblyName: "Test.BuildXL.Engine",
        rootNamespace: "Test.BuildXL.EngineTests",
        sources: globR(d`.`, "*.cs"),
        runTestArgs: {
            parallelGroups: categoriesToRunInParallel,
            testRunData: {
                MicrosoftNetCompilersSdkLocation: microsoftNetCompilerSpec,
            },
            tools: {
                exec: {
                    dependencies: [
                        microsoftNetCompilerSpec,
                        importFrom("Microsoft.Net.Compilers").Contents.all,
                        importFrom("Microsoft.NETCore.Compilers").Contents.all,
                    ]
                }
            }
        },    
        references: [
            EngineTestUtilities.dll,
            importFrom("BuildXL.Cache.VerticalStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Engine").Engine.dll,
            importFrom("BuildXL.Engine").Cache.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.FrontEnd").Core.dll,
            importFrom("BuildXL.FrontEnd").Script.dll,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            importFrom("BuildXL.FrontEndUnitTests").Core.dll,
            ...addIfLazy(BuildXLSdk.isFullFramework, () => [
                importFrom("Bond.Rpc.NET").pkg,
                importFrom("Bond.Core.NET").pkg
            ]),
        ],
        runtimeContent: [
            ...libsUsedForTesting,
            importFrom("BuildXL.Cache.VerticalStore").MemoizationStoreAdapter.dll,
        ],
    });
}
