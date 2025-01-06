// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as Managed from "Sdk.Managed.Shared";
import * as Nuget from "Sdk.BuildXL.Tools.NuGet";

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

    // Net8
    const net8WinX64ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ targetFramework: "net8.0", targetRuntime: "win-x64" });
    const net8OsxX64ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ targetFramework: "net8.0", targetRuntime: "osx-x64" });
    const net8WinX64MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ targetFramework: "net8.0", targetRuntime: "win-x64" });
    const net8OsxX64MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ targetFramework: "net8.0", targetRuntime: "osx-x64" });
    const net8WinX64DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "net8.0", targetRuntime: "win-x64" });
    const net8OsxX64DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "net8.0", targetRuntime: "osx-x64" });
    const net8WinX64CacheLogging = importFrom("BuildXL.Cache.Logging").Library.withQualifier({ targetFramework: "net8.0", targetRuntime: "win-x64" });
    const net8OsxX64CacheLogging = importFrom("BuildXL.Cache.Logging").Library.withQualifier({ targetFramework: "net8.0", targetRuntime: "osx-x64" });

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

    // ContentStore.Distributed
    export const contentStoreDistributed : Managed.Assembly[] = [
        net472ContentStore.Distributed.dll,
        netStandardContentStore.Distributed.dll,
        net6WinX64ContentStore.Distributed.dll,
        net8WinX64ContentStore.Distributed.dll,
    ];

    // ContentStore.Library
    export const contentStoreLibrary : Managed.Assembly[] = [
        net472ContentStore.Library.dll,
        netStandardContentStore.Library.dll,
        net6WinX64ContentStore.Library.dll,
        net8WinX64ContentStore.Library.dll,
    ];

    // ContentStore.Grpc
    export const contentStoreGrpc : Managed.Assembly[] = [
        net472ContentStore.Grpc.dll,
        netStandardContentStore.Grpc.dll,
        net6WinX64ContentStore.Grpc.dll,
        net8WinX64ContentStore.Grpc.dll,
    ];

    // ContentStore.Vsts
    export const contentStoreVsts : Managed.Assembly[] = [
        net472ContentStore.Vsts.dll,
        netStandardContentStore.Vsts.dll,
        net6WinX64ContentStore.Vsts.dll,
        net8WinX64ContentStore.Vsts.dll,
    ];

    // ContentStore.VstsInterfaces
    export const contentStoreVstsInterfaces : Managed.Assembly[] = [
        net472ContentStore.VstsInterfaces.dll,
        netStandardContentStore.VstsInterfaces.dll,
        net6WinX64ContentStore.VstsInterfaces.dll,
        net8WinX64ContentStore.VstsInterfaces.dll,
    ];

    // MemoizationStore.Distributed
    export const memoizationStoreDistributed : Managed.Assembly[] = [
        net472MemoizationStore.Distributed.dll,
        netStandardMemoizationStore.Distributed.dll,
        net6WinX64MemoizationStore.Distributed.dll,
        net8WinX64MemoizationStore.Distributed.dll,
    ];

    // MemoizationStore.Library
    export const memoizationStoreLibrary : Managed.Assembly[] = [
        net472MemoizationStore.Library.dll,
        netStandardMemoizationStore.Library.dll,
        net6WinX64MemoizationStore.Library.dll,
        net8WinX64MemoizationStore.Library.dll,
    ];

    // MemoizationStore.Vsts
    export const memoizationStoreVsts : Managed.Assembly[] = [
        net472MemoizationStore.Vsts.dll,
        netStandardMemoizationStore.Vsts.dll,
        net6WinX64MemoizationStore.Vsts.dll,
        net8WinX64MemoizationStore.Vsts.dll,
    ];

    // MemoizationStore.VstsInterfaces
    export const memoizationStoreVstsInterfaces : Managed.Assembly[] = [
        net472MemoizationStore.VstsInterfaces.dll,
        netStandardMemoizationStore.VstsInterfaces.dll,
        net6WinX64MemoizationStore.VstsInterfaces.dll,
        net8WinX64MemoizationStore.VstsInterfaces.dll,
    ];

    // BuildXL.Cache.Host.Services
    export const buildxlCacheHostServices : Managed.Assembly[] = [
        net472DistributedCacheHost.Service.dll,
        netStandardDistributedCacheHost.Service.dll,
        net6WinX64DistributedCacheHost.Service.dll,
        net8WinX64DistributedCacheHost.Service.dll,
    ];

    // BuildXL.Cache.Host.Configuration
    export const buildxlCacheHostConfiguration : Managed.Assembly[] = [
        net472DistributedCacheHost.Configuration.dll,
        netStandardDistributedCacheHost.Configuration.dll,
        net6WinX64DistributedCacheHost.Configuration.dll,
        net8WinX64DistributedCacheHost.Configuration.dll,
    ];

    // BuildXL.Cache.Logging
    export const buildxlCacheLogging : Managed.Assembly[] = [
        net472CacheLogging.dll,
        netStandardCacheLogging.dll,
        net6WinX64CacheLogging.dll,
        net8WinX64CacheLogging.dll,
    ];

    // ContentStore.Interfaces
    export const contentStoreInterfaces : Managed.Assembly[] = [
        net472ContentStore.Interfaces.dll,
        netStandardContentStore.Interfaces.dll,
        net6WinX64ContentStore.Interfaces.dll,
        net8WinX64ContentStore.Interfaces.dll,
    ];

    // MemoizationStore.Interfaces
    export const memoizationStoreInterfaces : Managed.Assembly[] = [
        net472MemoizationStore.Interfaces.dll,
        netStandardMemoizationStore.Interfaces.dll,
        net6WinX64MemoizationStore.Interfaces.dll,
        net8WinX64MemoizationStore.Interfaces.dll,
    ];

    // ContentStore.Hashing
    export const contentStoreHashing : Managed.Assembly[] = [
        net472ContentStore.Hashing.dll,
        netStandardContentStore.Hashing.dll,
        net6WinX64ContentStore.Hashing.dll,
        net8WinX64ContentStore.Hashing.dll,
    ];

    // ContentStore.UtilitiesCore
    export const contentStoreUtilitiesCore : Managed.Assembly[] = [
        net472ContentStore.UtilitiesCore.dll,
        netStandardContentStore.UtilitiesCore.dll,
        net6WinX64ContentStore.UtilitiesCore.dll,
        net8WinX64ContentStore.UtilitiesCore.dll,
    ];

    // BlobLifetimeManager.Library
    export const blobLifetimeManagerLibrary : Managed.Assembly[] = [
        importFrom("BuildXL.Cache.BlobLifetimeManager").Library.withQualifier({ targetFramework: "net6.0", targetRuntime: "win-x64" }).dll,
    ];

    // BuildCacheResourceHelper
    export const buildCacheResourceHelper : Managed.Assembly[] = [
        importFrom("BuildXL.Cache.BuildCacheResource").Helper.withQualifier({ targetFramework: "net472", targetRuntime: "win-x64" }).dll,
        importFrom("BuildXL.Cache.BuildCacheResource").Helper.withQualifier({ targetFramework: "netstandard2.0", targetRuntime: "win-x64" }).dll,
        importFrom("BuildXL.Cache.BuildCacheResource").Helper.withQualifier({ targetFramework: "net6.0", targetRuntime: "win-x64" }).dll,
        importFrom("BuildXL.Cache.BuildCacheResource").Helper.withQualifier({ targetFramework: "net8.0", targetRuntime: "win-x64" }).dll,
    ];

    // Old cache package deployments to be replaced by the ones above
    export const libraries : Deployment.Definition = {
        contents: [
            // ContentStore.Distributed
            Nuget.createAssemblyLayout(net472ContentStore.Distributed.dll),
            Nuget.createAssemblyLayout(netStandardContentStore.Distributed.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64ContentStore.Distributed.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64ContentStore.Distributed.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8WinX64ContentStore.Distributed.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8OsxX64ContentStore.Distributed.dll, "osx-x64", false),
            
            // ContentStore.Library
            Nuget.createAssemblyLayout(net472ContentStore.Library.dll),
            Nuget.createAssemblyLayout(netStandardContentStore.Library.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64ContentStore.Library.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64ContentStore.Library.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8WinX64ContentStore.Library.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8OsxX64ContentStore.Library.dll, "osx-x64", false),
            
            // ContentStore.Grpc
            Nuget.createAssemblyLayout(net472ContentStore.Grpc.dll),
            Nuget.createAssemblyLayout(netStandardContentStore.Grpc.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64ContentStore.Grpc.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64ContentStore.Grpc.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8WinX64ContentStore.Grpc.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8OsxX64ContentStore.Grpc.dll, "osx-x64", false),

            // ContentStore.Vsts
            ...addIfLazy(BuildXLSdk.Flags.isVstsArtifactsEnabled, () => [
                Nuget.createAssemblyLayout(net472ContentStore.Vsts.dll),
                Nuget.createAssemblyLayout(netStandardContentStore.Vsts.dll),
                Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64ContentStore.Vsts.dll, "win-x64", true),
                Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64ContentStore.Vsts.dll, "osx-x64", false),
                Nuget.createAssemblyLayoutWithSpecificRuntime(net8WinX64ContentStore.Vsts.dll, "win-x64", true),
                Nuget.createAssemblyLayoutWithSpecificRuntime(net8OsxX64ContentStore.Vsts.dll, "osx-x64", false),
            ]),

            // ContentStore.VstsInterfaces
            Nuget.createAssemblyLayout(net472ContentStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayout(netStandardContentStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64ContentStore.VstsInterfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64ContentStore.VstsInterfaces.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8WinX64ContentStore.VstsInterfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8OsxX64ContentStore.VstsInterfaces.dll, "osx-x64", false),

            // MemoizationStore.Distributed
            Nuget.createAssemblyLayout(net472MemoizationStore.Distributed.dll),
            Nuget.createAssemblyLayout(netStandardMemoizationStore.Distributed.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64MemoizationStore.Distributed.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64MemoizationStore.Distributed.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8WinX64MemoizationStore.Distributed.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8OsxX64MemoizationStore.Distributed.dll, "osx-x64", false),
            
            // MemoizationStore.Library
            Nuget.createAssemblyLayout(net472MemoizationStore.Library.dll),
            Nuget.createAssemblyLayout(netStandardMemoizationStore.Library.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64MemoizationStore.Library.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64MemoizationStore.Library.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8WinX64MemoizationStore.Library.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8OsxX64MemoizationStore.Library.dll, "osx-x64", false),

            // MemoizationStore.Vsts
            ...addIfLazy(BuildXLSdk.Flags.isVstsArtifactsEnabled, () => [
                Nuget.createAssemblyLayout(net472MemoizationStore.Vsts.dll),
                Nuget.createAssemblyLayout(netStandardMemoizationStore.Vsts.dll),
                Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64MemoizationStore.Vsts.dll, "win-x64", true),
                Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64MemoizationStore.Vsts.dll, "osx-x64", false),
                Nuget.createAssemblyLayoutWithSpecificRuntime(net8WinX64MemoizationStore.Vsts.dll, "win-x64", true),
                Nuget.createAssemblyLayoutWithSpecificRuntime(net8OsxX64MemoizationStore.Vsts.dll, "osx-x64", false),
            ]),

            // MemoizationStore.VstsInterfaces
            Nuget.createAssemblyLayout(net472MemoizationStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayout(netStandardMemoizationStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64MemoizationStore.VstsInterfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64MemoizationStore.VstsInterfaces.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8WinX64MemoizationStore.VstsInterfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8OsxX64MemoizationStore.VstsInterfaces.dll, "osx-x64", false),

            // BuildXL.Cache.Host.Services
            Nuget.createAssemblyLayout(net472DistributedCacheHost.Service.dll),
            Nuget.createAssemblyLayout(netStandardDistributedCacheHost.Service.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64DistributedCacheHost.Service.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64DistributedCacheHost.Service.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8WinX64DistributedCacheHost.Service.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8OsxX64DistributedCacheHost.Service.dll, "osx-x64", false),

            // BuildXL.Cache.Host.Configuration
            Nuget.createAssemblyLayout(net472DistributedCacheHost.Configuration.dll),
            Nuget.createAssemblyLayout(netStandardDistributedCacheHost.Configuration.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64DistributedCacheHost.Configuration.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64DistributedCacheHost.Configuration.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8WinX64DistributedCacheHost.Configuration.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8OsxX64DistributedCacheHost.Configuration.dll, "osx-x64", false),

            // BuildXL.Cache.Logging
            Nuget.createAssemblyLayout(net472CacheLogging.dll),
            Nuget.createAssemblyLayout(netStandardCacheLogging.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64CacheLogging.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64CacheLogging.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8WinX64CacheLogging.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8OsxX64CacheLogging.dll, "osx-x64", false),
        ]
    };

    export const interfaces : Deployment.Definition = {
        contents: [
            // ContentStore.Interfaces
            Nuget.createAssemblyLayout(net472ContentStore.Interfaces.dll),
            Nuget.createAssemblyLayout(netStandardContentStore.Interfaces.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64ContentStore.Interfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64ContentStore.Interfaces.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8WinX64ContentStore.Interfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8OsxX64ContentStore.Interfaces.dll, "osx-x64", false),
            Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.ContentStore").Interfaces.withQualifier(
                { targetFramework: "netstandard2.0", targetRuntime: "win-x64" }
            ).dll),

            // MemoizationStore.Interfaces
            Nuget.createAssemblyLayout(net472MemoizationStore.Interfaces.dll),
            Nuget.createAssemblyLayout(netStandardMemoizationStore.Interfaces.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64MemoizationStore.Interfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64MemoizationStore.Interfaces.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8WinX64MemoizationStore.Interfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8OsxX64MemoizationStore.Interfaces.dll, "osx-x64", false),
        ]
    };

    export const hashing : Deployment.Definition = {
        contents: [
            // ContentStore.Hashing
            Nuget.createAssemblyLayout(net472ContentStore.Hashing.dll),
            Nuget.createAssemblyLayout(netStandardContentStore.Hashing.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64ContentStore.Hashing.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64ContentStore.Hashing.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8WinX64ContentStore.Hashing.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8OsxX64ContentStore.Hashing.dll, "osx-x64", false),
            Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.ContentStore").Hashing.withQualifier(
                { targetFramework: "netstandard2.0", targetRuntime: "win-x64" }
            ).dll),

            // ContentStore.UtilitiesCore
            Nuget.createAssemblyLayout(net472ContentStore.UtilitiesCore.dll),
            Nuget.createAssemblyLayout(netStandardContentStore.UtilitiesCore.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6WinX64ContentStore.UtilitiesCore.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net6OsxX64ContentStore.UtilitiesCore.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8WinX64ContentStore.UtilitiesCore.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net8OsxX64ContentStore.UtilitiesCore.dll, "osx-x64", false),
            Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.withQualifier(
                { targetFramework: "netstandard2.0", targetRuntime: "win-x64" }
            ).dll),
        ]
    };
}