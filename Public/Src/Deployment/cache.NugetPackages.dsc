// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as Nuget from "Sdk.Managed.Tools.NuGet";

namespace Cache.NugetPackages {
    export declare const qualifier : { configuration: "debug" | "release"};

    const Net472ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ targetFramework: "net472", targetRuntime: "win-x64" });
    const Net462ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ targetFramework: "net462", targetRuntime: "win-x64" });
    const WinX64ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ targetFramework: "netcoreapp3.1", targetRuntime: "win-x64" });
    const OsxX64ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ targetFramework: "netcoreapp3.1", targetRuntime: "osx-x64" });

    const Net472MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ targetFramework: "net472", targetRuntime: "win-x64" });
    const Net462MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ targetFramework: "net462", targetRuntime: "win-x64" });
    const WinX64MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ targetFramework: "netcoreapp3.1", targetRuntime: "win-x64" });
    const OsxX64MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ targetFramework: "netcoreapp3.1", targetRuntime: "osx-x64" });

    const Net472DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "net472", targetRuntime: "win-x64" });
    const Net462DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "net462", targetRuntime: "win-x64" });
    const WinX64DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "netcoreapp3.1", targetRuntime: "win-x64" });
    const OsxX64DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "netcoreapp3.1", targetRuntime: "osx-x64" });

    const Net472CacheLogging = importFrom("BuildXL.Cache.Logging").Library.withQualifier({ targetFramework: "net472", targetRuntime: "win-x64" });
    const Net462CacheLogging = importFrom("BuildXL.Cache.Logging").Library.withQualifier({ targetFramework: "net462", targetRuntime: "win-x64" });
    const WinX64CacheLogging = importFrom("BuildXL.Cache.Logging").Library.withQualifier({ targetFramework: "netcoreapp3.1", targetRuntime: "win-x64" });
    const OsxX64CacheLogging = importFrom("BuildXL.Cache.Logging").Library.withQualifier({ targetFramework: "netcoreapp3.1", targetRuntime: "osx-x64" });

    export const tools : Deployment.Definition = {
        contents: [
            {
                subfolder: r`tools`,
                contents: [
                    Net472ContentStore.App.exe,
                    Net472MemoizationStore.App.exe,
                    Net472DistributedCacheHost.Configuration.dll,
                    Net472DistributedCacheHost.Service.dll,
                ]
            },
        ]
    };

    export const libraries : Deployment.Definition = {
        contents: [
            // ContentStore.Distributed
            Nuget.createAssemblyLayout(Net472ContentStore.Distributed.dll),
            Nuget.createAssemblyLayout(Net462ContentStore.Distributed.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(WinX64ContentStore.Distributed.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(OsxX64ContentStore.Distributed.dll, "osx-x64", false),
            // ContentStore.Library
            Nuget.createAssemblyLayout(Net472ContentStore.Library.dll),
            Nuget.createAssemblyLayout(Net462ContentStore.Library.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(WinX64ContentStore.Library.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(OsxX64ContentStore.Library.dll, "osx-x64", false),
            // ContentStore.Grpc
            Nuget.createAssemblyLayout(Net472ContentStore.Grpc.dll),
            Nuget.createAssemblyLayout(Net462ContentStore.Grpc.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(WinX64ContentStore.Grpc.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(OsxX64ContentStore.Grpc.dll, "osx-x64", false),

            // ContentStore.Vsts
            ...addIfLazy(BuildXLSdk.Flags.isVstsArtifactsEnabled, () => [
                Nuget.createAssemblyLayout(Net472ContentStore.Vsts.dll),
                Nuget.createAssemblyLayout(Net462ContentStore.Vsts.dll)
            ]),

            // ContentStore.VstsInterfaces
            Nuget.createAssemblyLayout(Net472ContentStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayout(Net462ContentStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(WinX64ContentStore.VstsInterfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(OsxX64ContentStore.VstsInterfaces.dll, "osx-x64", false),

            // MemoizationStore.Distributed
            Nuget.createAssemblyLayout(Net472MemoizationStore.Distributed.dll),
            Nuget.createAssemblyLayout(Net462MemoizationStore.Distributed.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(WinX64MemoizationStore.Distributed.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(OsxX64MemoizationStore.Distributed.dll, "osx-x64", false),
            // MemoizationStore.Library
            Nuget.createAssemblyLayout(Net472MemoizationStore.Library.dll),
            Nuget.createAssemblyLayout(Net462MemoizationStore.Library.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(WinX64MemoizationStore.Library.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(OsxX64MemoizationStore.Library.dll, "osx-x64", false),

            // MemoizationStore.Vsts
            ...addIfLazy(BuildXLSdk.Flags.isVstsArtifactsEnabled, () => [
                Nuget.createAssemblyLayout(Net472MemoizationStore.Vsts.dll),
                Nuget.createAssemblyLayout(Net462MemoizationStore.Vsts.dll)
            ]),

            // MemoizationStore.VstsInterfaces
            Nuget.createAssemblyLayout(Net472MemoizationStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayout(Net462MemoizationStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(WinX64MemoizationStore.VstsInterfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(OsxX64MemoizationStore.VstsInterfaces.dll, "osx-x64", false),

            // BuildXL.Cache.Host.Services
            Nuget.createAssemblyLayout(Net472DistributedCacheHost.Service.dll),
            Nuget.createAssemblyLayout(Net462DistributedCacheHost.Service.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(WinX64DistributedCacheHost.Service.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(OsxX64DistributedCacheHost.Service.dll, "osx-x64", false),

            // BuildXL.Cache.Host.Configuration
            Nuget.createAssemblyLayout(Net472DistributedCacheHost.Configuration.dll),
            Nuget.createAssemblyLayout(Net462DistributedCacheHost.Configuration.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(WinX64DistributedCacheHost.Configuration.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(OsxX64DistributedCacheHost.Configuration.dll, "osx-x64", false),

            // BuildXL.Cache.Logging
            Nuget.createAssemblyLayout(Net472CacheLogging.dll),
            Nuget.createAssemblyLayout(Net462CacheLogging.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(WinX64CacheLogging.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(OsxX64CacheLogging.dll, "osx-x64", false),
        ]
    };

    export const interfaces : Deployment.Definition = {
        contents: [
            // ContentStore.Interfaces
            Nuget.createAssemblyLayout(Net472ContentStore.Interfaces.dll),
            Nuget.createAssemblyLayout(Net462ContentStore.Interfaces.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(WinX64ContentStore.Interfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(OsxX64ContentStore.Interfaces.dll, "osx-x64", false),
            Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.ContentStore").Interfaces.withQualifier(
                { targetFramework: "netstandard2.0", targetRuntime: "win-x64" }
            ).dll),

            // MemoizationStore.Interfaces
            Nuget.createAssemblyLayout(Net472MemoizationStore.Interfaces.dll),
            Nuget.createAssemblyLayout(Net462MemoizationStore.Interfaces.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(WinX64MemoizationStore.Interfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(OsxX64MemoizationStore.Interfaces.dll, "osx-x64", false),
        ]
    };

    export const hashing : Deployment.Definition = {
        contents: [
            // ContentStore.Hashing
            Nuget.createAssemblyLayout(Net472ContentStore.Hashing.dll),
            Nuget.createAssemblyLayout(Net462ContentStore.Hashing.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(WinX64ContentStore.Hashing.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(OsxX64ContentStore.Hashing.dll, "osx-x64", false),
            Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.ContentStore").Hashing.withQualifier(
                { targetFramework: "netstandard2.0", targetRuntime: "win-x64" }
            ).dll),

            // ContentStore.UtilitiesCore
            Nuget.createAssemblyLayout(Net472ContentStore.UtilitiesCore.dll),
            Nuget.createAssemblyLayout(Net462ContentStore.UtilitiesCore.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(WinX64ContentStore.UtilitiesCore.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(OsxX64ContentStore.UtilitiesCore.dll, "osx-x64", false),
            Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.withQualifier(
                { targetFramework: "netstandard2.0", targetRuntime: "win-x64" }
            ).dll),
        ]
    };
}