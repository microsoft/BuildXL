// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as Nuget from "Sdk.Managed.Tools.NuGet";

namespace Cache.NugetPackages {
    export declare const qualifier : { configuration: "debug" | "release"};

    const net472ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ targetFramework: "net472", targetRuntime: "win-x64" });
    const netStandardContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ targetFramework: "netstandard2.0", targetRuntime: "win-x64" });
    const net6WinX64ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ targetFramework: "net6.0", targetRuntime: "win-x64" });
    const net6OsxX64ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ targetFramework: "net6.0", targetRuntime: "osx-x64" });

    const net472MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ targetFramework: "net472", targetRuntime: "win-x64" });
    const netStandardMemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ targetFramework: "netstandard2.0", targetRuntime: "win-x64" });
    const net6WinX64MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ targetFramework: "net6.0", targetRuntime: "win-x64" });
    const net6OsxX64MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ targetFramework: "net6.0", targetRuntime: "osx-x64" });

    const net472DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "net472", targetRuntime: "win-x64" });
    const netStandardDistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "netstandard2.0", targetRuntime: "win-x64" });
    const net6WinX64DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "net6.0", targetRuntime: "win-x64" });
    const net6OsxX64DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "net6.0", targetRuntime: "osx-x64" });

    const net472CacheLogging = importFrom("BuildXL.Cache.Logging").Library.withQualifier({ targetFramework: "net472", targetRuntime: "win-x64" });
    const netStandardCacheLogging = importFrom("BuildXL.Cache.Logging").Library.withQualifier({ targetFramework: "netstandard2.0", targetRuntime: "win-x64" });
    const net6WinX64CacheLogging = importFrom("BuildXL.Cache.Logging").Library.withQualifier({ targetFramework: "net6.0", targetRuntime: "win-x64" });
    const net6OsxX64CacheLogging = importFrom("BuildXL.Cache.Logging").Library.withQualifier({ targetFramework: "net6.0", targetRuntime: "osx-x64" });

    // Net7
    const net7WinX64ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ targetFramework: "net7.0", targetRuntime: "win-x64" });
    const net7OsxX64ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ targetFramework: "net7.0", targetRuntime: "osx-x64" });
    const net7WinX64MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ targetFramework: "net7.0", targetRuntime: "win-x64" });
    const net7OsxX64MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ targetFramework: "net7.0", targetRuntime: "osx-x64" });
    const net7WinX64DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "net7.0", targetRuntime: "win-x64" });
    const net7OsxX64DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "net7.0", targetRuntime: "osx-x64" });
    const net7WinX64CacheLogging = importFrom("BuildXL.Cache.Logging").Library.withQualifier({ targetFramework: "net7.0", targetRuntime: "win-x64" });
    const net7OsxX64CacheLogging = importFrom("BuildXL.Cache.Logging").Library.withQualifier({ targetFramework: "net7.0", targetRuntime: "osx-x64" });


    export const tools : Deployment.Definition = {
        contents: [
            {
                subfolder: r`tools`,
                contents: [
                    net472DistributedCacheHost.Configuration.dll,
                    net472ContentStore.App.exe,
                    net472DistributedCacheHost.Service.dll
                ]
            },
        ]
    };

    export const libraries : Deployment.Definition = {
        contents: [
            // ContentStore.Distributed
            Nuget.createAssemblyLayout(net472ContentStore.Distributed.dll),
            Nuget.createAssemblyLayout(netStandardContentStore.Distributed.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64ContentStore.Distributed.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64ContentStore.Distributed.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7WinX64ContentStore.Distributed.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7OsxX64ContentStore.Distributed.dll, "osx-x64", false),
            
            // ContentStore.Library
            Nuget.createAssemblyLayout(net472ContentStore.Library.dll),
            Nuget.createAssemblyLayout(netStandardContentStore.Library.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64ContentStore.Library.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64ContentStore.Library.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7WinX64ContentStore.Library.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7OsxX64ContentStore.Library.dll, "osx-x64", false),
            
            // ContentStore.Grpc
            Nuget.createAssemblyLayout(net472ContentStore.Grpc.dll),
            Nuget.createAssemblyLayout(netStandardContentStore.Grpc.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64ContentStore.Grpc.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64ContentStore.Grpc.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7WinX64ContentStore.Grpc.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7OsxX64ContentStore.Grpc.dll, "osx-x64", false),

            // ContentStore.Vsts
            ...addIfLazy(BuildXLSdk.Flags.isVstsArtifactsEnabled, () => [
                Nuget.createAssemblyLayout(net472ContentStore.Vsts.dll),
                Nuget.createAssemblyLayout(netStandardContentStore.Vsts.dll),
                Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64ContentStore.Vsts.dll, "win-x64", true),
                Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64ContentStore.Vsts.dll, "osx-x64", false),
                Nuget.createAssemblyLayoutWithSpecificRuntime(net7WinX64ContentStore.Vsts.dll, "win-x64", true),
                Nuget.createAssemblyLayoutWithSpecificRuntime(net7OsxX64ContentStore.Vsts.dll, "osx-x64", false)
            ]),

            // ContentStore.VstsInterfaces
            Nuget.createAssemblyLayout(net472ContentStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayout(netStandardContentStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64ContentStore.VstsInterfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64ContentStore.VstsInterfaces.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7WinX64ContentStore.VstsInterfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7OsxX64ContentStore.VstsInterfaces.dll, "osx-x64", false),

            // MemoizationStore.Distributed
            Nuget.createAssemblyLayout(net472MemoizationStore.Distributed.dll),
            Nuget.createAssemblyLayout(netStandardMemoizationStore.Distributed.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64MemoizationStore.Distributed.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64MemoizationStore.Distributed.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7WinX64MemoizationStore.Distributed.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7OsxX64MemoizationStore.Distributed.dll, "osx-x64", false),
            
            // MemoizationStore.Library
            Nuget.createAssemblyLayout(net472MemoizationStore.Library.dll),
            Nuget.createAssemblyLayout(netStandardMemoizationStore.Library.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64MemoizationStore.Library.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64MemoizationStore.Library.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7WinX64MemoizationStore.Library.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7OsxX64MemoizationStore.Library.dll, "osx-x64", false),

            // MemoizationStore.Vsts
            ...addIfLazy(BuildXLSdk.Flags.isVstsArtifactsEnabled, () => [
                Nuget.createAssemblyLayout(net472MemoizationStore.Vsts.dll),
                Nuget.createAssemblyLayout(netStandardMemoizationStore.Vsts.dll),
                Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64MemoizationStore.Vsts.dll, "win-x64", true),
                Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64MemoizationStore.Vsts.dll, "osx-x64", false),
                Nuget.createAssemblyLayoutWithSpecificRuntime(net7WinX64MemoizationStore.Vsts.dll, "win-x64", true),
                Nuget.createAssemblyLayoutWithSpecificRuntime(net7OsxX64MemoizationStore.Vsts.dll, "osx-x64", false),
            ]),

            // MemoizationStore.VstsInterfaces
            Nuget.createAssemblyLayout(net472MemoizationStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayout(netStandardMemoizationStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64MemoizationStore.VstsInterfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64MemoizationStore.VstsInterfaces.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7WinX64MemoizationStore.VstsInterfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7OsxX64MemoizationStore.VstsInterfaces.dll, "osx-x64", false),

            // BuildXL.Cache.Host.Services
            Nuget.createAssemblyLayout(net472DistributedCacheHost.Service.dll),
            Nuget.createAssemblyLayout(netStandardDistributedCacheHost.Service.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64DistributedCacheHost.Service.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64DistributedCacheHost.Service.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7WinX64DistributedCacheHost.Service.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7OsxX64DistributedCacheHost.Service.dll, "osx-x64", false),

            // BuildXL.Cache.Host.Configuration
            Nuget.createAssemblyLayout(net472DistributedCacheHost.Configuration.dll),
            Nuget.createAssemblyLayout(netStandardDistributedCacheHost.Configuration.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64DistributedCacheHost.Configuration.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64DistributedCacheHost.Configuration.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7WinX64DistributedCacheHost.Configuration.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7OsxX64DistributedCacheHost.Configuration.dll, "osx-x64", false),

            // BuildXL.Cache.Logging
            Nuget.createAssemblyLayout(net472CacheLogging.dll),
            Nuget.createAssemblyLayout(netStandardCacheLogging.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64CacheLogging.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64CacheLogging.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7WinX64CacheLogging.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7OsxX64CacheLogging.dll, "osx-x64", false),
        ]
    };

    export const interfaces : Deployment.Definition = {
        contents: [
            // ContentStore.Interfaces
            Nuget.createAssemblyLayout(net472ContentStore.Interfaces.dll),
            Nuget.createAssemblyLayout(netStandardContentStore.Interfaces.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64ContentStore.Interfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64ContentStore.Interfaces.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7WinX64ContentStore.Interfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7OsxX64ContentStore.Interfaces.dll, "osx-x64", false),
            Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.ContentStore").Interfaces.withQualifier(
                { targetFramework: "netstandard2.0", targetRuntime: "win-x64" }
            ).dll),

            // MemoizationStore.Interfaces
            Nuget.createAssemblyLayout(net472MemoizationStore.Interfaces.dll),
            Nuget.createAssemblyLayout(netStandardMemoizationStore.Interfaces.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64MemoizationStore.Interfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64MemoizationStore.Interfaces.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7WinX64MemoizationStore.Interfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7OsxX64MemoizationStore.Interfaces.dll, "osx-x64", false),
        ]
    };

    export const hashing : Deployment.Definition = {
        contents: [
            // ContentStore.Hashing
            Nuget.createAssemblyLayout(net472ContentStore.Hashing.dll),
            Nuget.createAssemblyLayout(netStandardContentStore.Hashing.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64ContentStore.Hashing.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64ContentStore.Hashing.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7WinX64ContentStore.Hashing.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7OsxX64ContentStore.Hashing.dll, "osx-x64", false),
            Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.ContentStore").Hashing.withQualifier(
                { targetFramework: "netstandard2.0", targetRuntime: "win-x64" }
            ).dll),

            // ContentStore.UtilitiesCore
            Nuget.createAssemblyLayout(net472ContentStore.UtilitiesCore.dll),
            Nuget.createAssemblyLayout(netStandardContentStore.UtilitiesCore.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64ContentStore.UtilitiesCore.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64ContentStore.UtilitiesCore.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7WinX64ContentStore.UtilitiesCore.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net7OsxX64ContentStore.UtilitiesCore.dll, "osx-x64", false),
            Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.withQualifier(
                { targetFramework: "netstandard2.0", targetRuntime: "win-x64" }
            ).dll),
        ]
    };
}