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

    const net472MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ targetFramework: "net472", targetRuntime: "win-x64" });
    const netStandardMemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ targetFramework: "netstandard2.0", targetRuntime: "win-x64" });

    const net472DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "net472", targetRuntime: "win-x64" });
    const netStandardDistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "netstandard2.0", targetRuntime: "win-x64" });

    const net472CacheLogging = importFrom("BuildXL.Cache.Logging").Library.withQualifier({ targetFramework: "net472", targetRuntime: "win-x64" });
    const netStandardCacheLogging = importFrom("BuildXL.Cache.Logging").Library.withQualifier({ targetFramework: "netstandard2.0", targetRuntime: "win-x64" });

    // Net8
    const net8WinX64ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ targetFramework: "net8.0", targetRuntime: "win-x64" });
    const net8OsxX64ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ targetFramework: "net8.0", targetRuntime: "osx-x64" });
    const net8WinX64MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ targetFramework: "net8.0", targetRuntime: "win-x64" });
    const net8OsxX64MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ targetFramework: "net8.0", targetRuntime: "osx-x64" });
    const net8WinX64DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "net8.0", targetRuntime: "win-x64" });
    const net8OsxX64DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "net8.0", targetRuntime: "osx-x64" });
    const net8WinX64CacheLogging = importFrom("BuildXL.Cache.Logging").Library.withQualifier({ targetFramework: "net8.0", targetRuntime: "win-x64" });
    const net8OsxX64CacheLogging = importFrom("BuildXL.Cache.Logging").Library.withQualifier({ targetFramework: "net8.0", targetRuntime: "osx-x64" });

    // Net9
    const net9WinX64ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ targetFramework: "net9.0", targetRuntime: "win-x64" });
    const net9OsxX64ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ targetFramework: "net9.0", targetRuntime: "osx-x64" });
    const net9WinX64MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ targetFramework: "net9.0", targetRuntime: "win-x64" });
    const net9OsxX64MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ targetFramework: "net9.0", targetRuntime: "osx-x64" });
    const net9WinX64DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "net9.0", targetRuntime: "win-x64" });
    const net9OsxX64DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "net9.0", targetRuntime: "osx-x64" });
    const net9WinX64CacheLogging = importFrom("BuildXL.Cache.Logging").Library.withQualifier({ targetFramework: "net9.0", targetRuntime: "win-x64" });
    const net9OsxX64CacheLogging = importFrom("BuildXL.Cache.Logging").Library.withQualifier({ targetFramework: "net9.0", targetRuntime: "osx-x64" });

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
        net8WinX64ContentStore.Distributed.dll,
        net9WinX64ContentStore.Distributed.dll,
    ];

    // ContentStore.Library
    export const contentStoreLibrary : Managed.Assembly[] = [
        net472ContentStore.Library.dll,
        netStandardContentStore.Library.dll,
        net8WinX64ContentStore.Library.dll,
        net9WinX64ContentStore.Library.dll,
    ];

    // ContentStore.Grpc
    export const contentStoreGrpc : Managed.Assembly[] = [
        net472ContentStore.Grpc.dll,
        netStandardContentStore.Grpc.dll,
        net8WinX64ContentStore.Grpc.dll,
        net9WinX64ContentStore.Grpc.dll,
    ];

    // ContentStore.Vsts
    export const contentStoreVsts : Managed.Assembly[] = [
        net472ContentStore.Vsts.dll,
        netStandardContentStore.Vsts.dll,
        net8WinX64ContentStore.Vsts.dll,
        net9WinX64ContentStore.Vsts.dll,
    ];

    // ContentStore.VstsInterfaces
    export const contentStoreVstsInterfaces : Managed.Assembly[] = [
        net472ContentStore.VstsInterfaces.dll,
        netStandardContentStore.VstsInterfaces.dll,
        net8WinX64ContentStore.VstsInterfaces.dll,
        net9WinX64ContentStore.VstsInterfaces.dll,
    ];

    // MemoizationStore.Distributed
    export const memoizationStoreDistributed : Managed.Assembly[] = [
        net472MemoizationStore.Distributed.dll,
        netStandardMemoizationStore.Distributed.dll,
        net8WinX64MemoizationStore.Distributed.dll,
        net9WinX64MemoizationStore.Distributed.dll,
    ];

    // MemoizationStore.Library
    export const memoizationStoreLibrary : Managed.Assembly[] = [
        net472MemoizationStore.Library.dll,
        netStandardMemoizationStore.Library.dll,
        net8WinX64MemoizationStore.Library.dll,
        net9WinX64MemoizationStore.Library.dll,
    ];

    // MemoizationStore.Vsts
    export const memoizationStoreVsts : Managed.Assembly[] = [
        net472MemoizationStore.Vsts.dll,
        netStandardMemoizationStore.Vsts.dll,
        net8WinX64MemoizationStore.Vsts.dll,
        net9WinX64MemoizationStore.Vsts.dll,
    ];

    // MemoizationStore.VstsInterfaces
    export const memoizationStoreVstsInterfaces : Managed.Assembly[] = [
        net472MemoizationStore.VstsInterfaces.dll,
        netStandardMemoizationStore.VstsInterfaces.dll,
        net8WinX64MemoizationStore.VstsInterfaces.dll,
        net9WinX64MemoizationStore.VstsInterfaces.dll,
    ];

    // BuildXL.Cache.Host.Services
    export const buildxlCacheHostServices : Managed.Assembly[] = [
        net472DistributedCacheHost.Service.dll,
        netStandardDistributedCacheHost.Service.dll,
        net8WinX64DistributedCacheHost.Service.dll,
        net9WinX64DistributedCacheHost.Service.dll,
    ];

    // BuildXL.Cache.Host.Configuration
    export const buildxlCacheHostConfiguration : Managed.Assembly[] = [
        net472DistributedCacheHost.Configuration.dll,
        netStandardDistributedCacheHost.Configuration.dll,
        net8WinX64DistributedCacheHost.Configuration.dll,
        net9WinX64DistributedCacheHost.Configuration.dll,
    ];

    // BuildXL.Cache.Logging
    export const buildxlCacheLogging : Managed.Assembly[] = [
        net472CacheLogging.dll,
        netStandardCacheLogging.dll,
        net8WinX64CacheLogging.dll,
        net9WinX64CacheLogging.dll,
    ];

    // ContentStore.Interfaces
    export const contentStoreInterfaces : Managed.Assembly[] = [
        net472ContentStore.Interfaces.dll,
        netStandardContentStore.Interfaces.dll,
        net8WinX64ContentStore.Interfaces.dll,
        net9WinX64ContentStore.Interfaces.dll,
    ];

    // MemoizationStore.Interfaces
    export const memoizationStoreInterfaces : Managed.Assembly[] = [
        net472MemoizationStore.Interfaces.dll,
        netStandardMemoizationStore.Interfaces.dll,
        net8WinX64MemoizationStore.Interfaces.dll,
        net9WinX64MemoizationStore.Interfaces.dll,
    ];

    // ContentStore.Hashing
    export const contentStoreHashing : Managed.Assembly[] = [
        net472ContentStore.Hashing.dll,
        netStandardContentStore.Hashing.dll,
        net8WinX64ContentStore.Hashing.dll,
        net9WinX64ContentStore.Hashing.dll,
    ];

    // ContentStore.UtilitiesCore
    export const contentStoreUtilitiesCore : Managed.Assembly[] = [
        net472ContentStore.UtilitiesCore.dll,
        netStandardContentStore.UtilitiesCore.dll,
        net8WinX64ContentStore.UtilitiesCore.dll,
        net9WinX64ContentStore.UtilitiesCore.dll,
    ];

    // BlobLifetimeManager.Library
    export const blobLifetimeManagerLibrary : Managed.Assembly[] = [
        importFrom("BuildXL.Cache.BlobLifetimeManager").Library.withQualifier({ targetFramework: "net8.0", targetRuntime: "win-x64" }).dll,
    ];

    // BuildCacheResourceHelper
    export const buildCacheResourceHelper : Managed.Assembly[] = [
        importFrom("BuildXL.Cache.BuildCacheResource").Helper.withQualifier({ targetFramework: "net472", targetRuntime: "win-x64" }).dll,
        importFrom("BuildXL.Cache.BuildCacheResource").Helper.withQualifier({ targetFramework: "netstandard2.0", targetRuntime: "win-x64" }).dll,
        importFrom("BuildXL.Cache.BuildCacheResource").Helper.withQualifier({ targetFramework: "net8.0", targetRuntime: "win-x64" }).dll,
        importFrom("BuildXL.Cache.BuildCacheResource").Helper.withQualifier({ targetFramework: "net9.0", targetRuntime: "win-x64" }).dll,
    ];
}