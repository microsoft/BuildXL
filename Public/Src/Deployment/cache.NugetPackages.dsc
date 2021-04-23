// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as Nuget from "Sdk.Managed.Tools.NuGet";

namespace Cache.NugetPackages {
    export declare const qualifier : { configuration: "debug" | "release"};

    const net472ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ targetFramework: "net472", targetRuntime: "win-x64" });
    const net462ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ targetFramework: "net462", targetRuntime: "win-x64" });
    const netStandardContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ targetFramework: "netstandard2.0", targetRuntime: "win-x64" });
    const winX64ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ targetFramework: "netcoreapp3.1", targetRuntime: "win-x64" });
    const osxX64ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ targetFramework: "netcoreapp3.1", targetRuntime: "osx-x64" });
    const net5WinX64ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ targetFramework: "net5.0", targetRuntime: "win-x64" });
    const net5OsxX64ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ targetFramework: "net5.0", targetRuntime: "osx-x64" });

    const net472MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ targetFramework: "net472", targetRuntime: "win-x64" });
    const net462MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ targetFramework: "net462", targetRuntime: "win-x64" });
    const netStandardMemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ targetFramework: "netstandard2.0", targetRuntime: "win-x64" });
    const winX64MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ targetFramework: "netcoreapp3.1", targetRuntime: "win-x64" });
    const osxX64MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ targetFramework: "netcoreapp3.1", targetRuntime: "osx-x64" });    
    const net5WinX64MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ targetFramework: "net5.0", targetRuntime: "win-x64" });
    const net5OsxX64MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ targetFramework: "net5.0", targetRuntime: "osx-x64" });

    const net472DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "net472", targetRuntime: "win-x64" });
    const net462DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "net462", targetRuntime: "win-x64" });
    const netStandardDistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "netstandard2.0", targetRuntime: "win-x64" });
    const winX64DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "netcoreapp3.1", targetRuntime: "win-x64" });
    const osxX64DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "netcoreapp3.1", targetRuntime: "osx-x64" });
    const net5WinX64DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "net5.0", targetRuntime: "win-x64" });
    const net5OsxX64DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "net5.0", targetRuntime: "osx-x64" });

    const net472CacheLogging = importFrom("BuildXL.Cache.Logging").Library.withQualifier({ targetFramework: "net472", targetRuntime: "win-x64" });
    const netStandardCacheLogging = importFrom("BuildXL.Cache.Logging").Library.withQualifier({ targetFramework: "netstandard2.0", targetRuntime: "win-x64" });
    const winX64CacheLogging = importFrom("BuildXL.Cache.Logging").Library.withQualifier({ targetFramework: "netcoreapp3.1", targetRuntime: "win-x64" });
    const osxX64CacheLogging = importFrom("BuildXL.Cache.Logging").Library.withQualifier({ targetFramework: "netcoreapp3.1", targetRuntime: "osx-x64" });
    const net5WinX64CacheLogging = importFrom("BuildXL.Cache.Logging").Library.withQualifier({ targetFramework: "net5.0", targetRuntime: "win-x64" });
    const net5OsxX64CacheLogging = importFrom("BuildXL.Cache.Logging").Library.withQualifier({ targetFramework: "net5.0", targetRuntime: "osx-x64" });

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
            Nuget.createAssemblyLayoutWithSpecificRuntime(winX64ContentStore.Distributed.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(osxX64ContentStore.Distributed.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5WinX64ContentStore.Distributed.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5OsxX64ContentStore.Distributed.dll, "osx-x64", false),
            
            // ContentStore.Library
            Nuget.createAssemblyLayout(net472ContentStore.Library.dll),
            Nuget.createAssemblyLayout(net462ContentStore.Library.dll),
            Nuget.createAssemblyLayout(netStandardContentStore.Library.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(winX64ContentStore.Library.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(osxX64ContentStore.Library.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5WinX64ContentStore.Library.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5OsxX64ContentStore.Library.dll, "osx-x64", false),
            
            // ContentStore.VfsLibraries
            Nuget.createAssemblyLayout(net472ContentStore.VfsLibrary.dll),
            Nuget.createAssemblyLayout(netStandardContentStore.VfsLibrary.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(winX64ContentStore.VfsLibrary.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5WinX64ContentStore.VfsLibrary.dll, "win-x64", true),

            // ContentStore.Grpc
            Nuget.createAssemblyLayout(net472ContentStore.Grpc.dll),
            Nuget.createAssemblyLayout(net462ContentStore.Grpc.dll),
            Nuget.createAssemblyLayout(netStandardContentStore.Grpc.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(winX64ContentStore.Grpc.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(osxX64ContentStore.Grpc.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5WinX64ContentStore.Grpc.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5OsxX64ContentStore.Grpc.dll, "osx-x64", false),

            // ContentStore.Vsts
            ...addIfLazy(BuildXLSdk.Flags.isVstsArtifactsEnabled, () => [
                Nuget.createAssemblyLayout(net472ContentStore.Vsts.dll),
                Nuget.createAssemblyLayout(netStandardContentStore.Vsts.dll)
            ]),

            // ContentStore.VstsInterfaces
            Nuget.createAssemblyLayout(net472ContentStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayout(net462ContentStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayout(netStandardContentStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(winX64ContentStore.VstsInterfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(osxX64ContentStore.VstsInterfaces.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5WinX64ContentStore.VstsInterfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5OsxX64ContentStore.VstsInterfaces.dll, "osx-x64", false),

            // MemoizationStore.Distributed
            Nuget.createAssemblyLayout(net472MemoizationStore.Distributed.dll),
            Nuget.createAssemblyLayout(netStandardMemoizationStore.Distributed.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(winX64MemoizationStore.Distributed.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(osxX64MemoizationStore.Distributed.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5WinX64MemoizationStore.Distributed.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5OsxX64MemoizationStore.Distributed.dll, "osx-x64", false),
            
            // MemoizationStore.Library
            Nuget.createAssemblyLayout(net472MemoizationStore.Library.dll),
            Nuget.createAssemblyLayout(netStandardMemoizationStore.Library.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(winX64MemoizationStore.Library.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(osxX64MemoizationStore.Library.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5WinX64MemoizationStore.Library.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5OsxX64MemoizationStore.Library.dll, "osx-x64", false),

            // MemoizationStore.Vsts
            ...addIfLazy(BuildXLSdk.Flags.isVstsArtifactsEnabled, () => [
                Nuget.createAssemblyLayout(net472MemoizationStore.Vsts.dll),
                Nuget.createAssemblyLayout(netStandardMemoizationStore.Vsts.dll)
            ]),

            // MemoizationStore.VstsInterfaces
            Nuget.createAssemblyLayout(net472MemoizationStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayout(net462MemoizationStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayout(netStandardMemoizationStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(winX64MemoizationStore.VstsInterfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(osxX64MemoizationStore.VstsInterfaces.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5WinX64MemoizationStore.VstsInterfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5OsxX64MemoizationStore.VstsInterfaces.dll, "osx-x64", false),

            // BuildXL.Cache.Host.Services
            Nuget.createAssemblyLayout(net472DistributedCacheHost.Service.dll),
            Nuget.createAssemblyLayout(netStandardDistributedCacheHost.Service.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(winX64DistributedCacheHost.Service.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(osxX64DistributedCacheHost.Service.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5WinX64DistributedCacheHost.Service.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5OsxX64DistributedCacheHost.Service.dll, "osx-x64", false),

            // BuildXL.Cache.Host.Configuration
            Nuget.createAssemblyLayout(net472DistributedCacheHost.Configuration.dll),
            Nuget.createAssemblyLayout(net462DistributedCacheHost.Configuration.dll),
            Nuget.createAssemblyLayout(netStandardDistributedCacheHost.Configuration.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(winX64DistributedCacheHost.Configuration.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(osxX64DistributedCacheHost.Configuration.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5WinX64DistributedCacheHost.Configuration.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5OsxX64DistributedCacheHost.Configuration.dll, "osx-x64", false),

            // BuildXL.Cache.Logging
            Nuget.createAssemblyLayout(net472CacheLogging.dll),
            Nuget.createAssemblyLayout(netStandardCacheLogging.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(winX64CacheLogging.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(osxX64CacheLogging.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5WinX64CacheLogging.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5OsxX64CacheLogging.dll, "osx-x64", false),
        ]
    };

    export const interfaces : Deployment.Definition = {
        contents: [
            // ContentStore.Interfaces
            Nuget.createAssemblyLayout(net472ContentStore.Interfaces.dll),
            Nuget.createAssemblyLayout(net462ContentStore.Interfaces.dll),
            Nuget.createAssemblyLayout(netStandardContentStore.Interfaces.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(winX64ContentStore.Interfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(osxX64ContentStore.Interfaces.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5WinX64ContentStore.Interfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5OsxX64ContentStore.Interfaces.dll, "osx-x64", false),
            Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.ContentStore").Interfaces.withQualifier(
                { targetFramework: "netstandard2.0", targetRuntime: "win-x64" }
            ).dll),

            // MemoizationStore.Interfaces
            Nuget.createAssemblyLayout(net472MemoizationStore.Interfaces.dll),
            Nuget.createAssemblyLayout(net462MemoizationStore.Interfaces.dll),
            Nuget.createAssemblyLayout(netStandardMemoizationStore.Interfaces.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(winX64MemoizationStore.Interfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(osxX64MemoizationStore.Interfaces.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5WinX64MemoizationStore.Interfaces.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5OsxX64MemoizationStore.Interfaces.dll, "osx-x64", false),
        ]
    };

    export const hashing : Deployment.Definition = {
        contents: [
            // ContentStore.Hashing
            Nuget.createAssemblyLayout(net472ContentStore.Hashing.dll),
            Nuget.createAssemblyLayout(net462ContentStore.Hashing.dll),
            Nuget.createAssemblyLayout(netStandardContentStore.Hashing.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(winX64ContentStore.Hashing.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(osxX64ContentStore.Hashing.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5WinX64ContentStore.Hashing.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5OsxX64ContentStore.Hashing.dll, "osx-x64", false),
            Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.ContentStore").Hashing.withQualifier(
                { targetFramework: "netstandard2.0", targetRuntime: "win-x64" }
            ).dll),

            // ContentStore.UtilitiesCore
            Nuget.createAssemblyLayout(net472ContentStore.UtilitiesCore.dll),
            Nuget.createAssemblyLayout(net462ContentStore.UtilitiesCore.dll),
            Nuget.createAssemblyLayout(netStandardContentStore.UtilitiesCore.dll),
            Nuget.createAssemblyLayoutWithSpecificRuntime(winX64ContentStore.UtilitiesCore.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(osxX64ContentStore.UtilitiesCore.dll, "osx-x64", false),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5WinX64ContentStore.UtilitiesCore.dll, "win-x64", true),
            Nuget.createAssemblyLayoutWithSpecificRuntime(net5OsxX64ContentStore.UtilitiesCore.dll, "osx-x64", false),
            Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.withQualifier(
                { targetFramework: "netstandard2.0", targetRuntime: "win-x64" }
            ).dll),
        ]
    };
}