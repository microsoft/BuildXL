// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as Nuget from "Sdk.Managed.Tools.NuGet";

namespace Cache.NugetPackages {
    export declare const qualifier : { configuration: "debug" | "release"};

    const Net451ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ configuration: qualifier.configuration, targetFramework: "net451", targetRuntime: "win-x64" });
    const Net472ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ configuration: qualifier.configuration, targetFramework: "net472", targetRuntime: "win-x64" });
    const WinX64ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ configuration: qualifier.configuration, targetFramework: "netcoreapp3.0", targetRuntime: "win-x64" });
    const OsxX64ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ configuration: qualifier.configuration, targetFramework: "netcoreapp3.0", targetRuntime: "osx-x64" });

    const Net451MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ configuration: qualifier.configuration, targetFramework: "net451", targetRuntime: "win-x64" });
    const Net472MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ configuration: qualifier.configuration, targetFramework: "net472", targetRuntime: "win-x64" });
    const WinX64MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ configuration: qualifier.configuration, targetFramework: "netcoreapp3.0", targetRuntime: "win-x64" });
    const OsxX64MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ configuration: qualifier.configuration, targetFramework: "netcoreapp3.0", targetRuntime: "osx-x64" });

    const Net472DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ configuration: qualifier.configuration, targetFramework: "net472", targetRuntime: "win-x64" });
    const WinX64DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ configuration: qualifier.configuration, targetFramework: "netcoreapp3.0", targetRuntime: "win-x64" });
    const OsxX64DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ configuration: qualifier.configuration, targetFramework: "netcoreapp3.0", targetRuntime: "osx-x64" });

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
            Nuget.createAssemblyLayout(Net451ContentStore.Distributed.dll),
            Nuget.createAssemblyLayout(Net472ContentStore.Distributed.dll),
            Nuget.createAssemblyLayoutWithSubfolder(WinX64ContentStore.Distributed.dll, "win-x64"),
            Nuget.createAssemblyLayoutWithSubfolder(OsxX64ContentStore.Distributed.dll, "osx-x64"),
            // ContentStore.Library
            Nuget.createAssemblyLayout(Net451ContentStore.Library.dll),
            Nuget.createAssemblyLayout(Net472ContentStore.Library.dll),
            Nuget.createAssemblyLayoutWithSubfolder(WinX64ContentStore.Library.dll, "win-x64"),
            Nuget.createAssemblyLayoutWithSubfolder(OsxX64ContentStore.Library.dll, "osx-x64"),
            // ContentStore.Grpc
            Nuget.createAssemblyLayout(Net451ContentStore.Grpc.dll),
            Nuget.createAssemblyLayout(Net472ContentStore.Grpc.dll),
            Nuget.createAssemblyLayoutWithSubfolder(WinX64ContentStore.Grpc.dll, "win-x64"),
            Nuget.createAssemblyLayoutWithSubfolder(OsxX64ContentStore.Grpc.dll, "osx-x64"),

            // ContentStore.Vsts
            ...addIfLazy(BuildXLSdk.Flags.isVstsArtifactsEnabled, () => [
                Nuget.createAssemblyLayout(Net451ContentStore.Vsts.dll),
                Nuget.createAssemblyLayout(Net472ContentStore.Vsts.dll)
            ]),

            // ContentStore.VstsInterfaces
            Nuget.createAssemblyLayout(Net451ContentStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayout(Net472ContentStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayoutWithSubfolder(WinX64ContentStore.VstsInterfaces.dll, "win-x64"),
            Nuget.createAssemblyLayoutWithSubfolder(OsxX64ContentStore.VstsInterfaces.dll, "osx-x64"),

            // MemoizationStore.Distributed
            Nuget.createAssemblyLayout(Net451MemoizationStore.Distributed.dll),
            Nuget.createAssemblyLayout(Net472MemoizationStore.Distributed.dll),
            Nuget.createAssemblyLayoutWithSubfolder(WinX64MemoizationStore.Distributed.dll, "win-x64"),
            Nuget.createAssemblyLayoutWithSubfolder(OsxX64MemoizationStore.Distributed.dll, "osx-x64"),
            // MemoizationStore.Library
            Nuget.createAssemblyLayout(Net451MemoizationStore.Library.dll),
            Nuget.createAssemblyLayout(Net472MemoizationStore.Library.dll),
            Nuget.createAssemblyLayoutWithSubfolder(WinX64MemoizationStore.Library.dll, "win-x64"),
            Nuget.createAssemblyLayoutWithSubfolder(OsxX64MemoizationStore.Library.dll, "osx-x64"),

            // MemoizationStore.Vsts
            ...addIfLazy(BuildXLSdk.Flags.isVstsArtifactsEnabled, () => [
                Nuget.createAssemblyLayout(Net451MemoizationStore.Vsts.dll),
                Nuget.createAssemblyLayout(Net472MemoizationStore.Vsts.dll)
            ]),

            // MemoizationStore.VstsInterfaces
            Nuget.createAssemblyLayout(Net451MemoizationStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayout(Net472MemoizationStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayoutWithSubfolder(WinX64MemoizationStore.VstsInterfaces.dll, "win-x64"),
            Nuget.createAssemblyLayoutWithSubfolder(OsxX64MemoizationStore.VstsInterfaces.dll, "osx-x64"),

            // BuildXL.Cache.Host.Services
            Nuget.createAssemblyLayout(Net472DistributedCacheHost.Service.dll),
            Nuget.createAssemblyLayoutWithSubfolder(WinX64DistributedCacheHost.Service.dll, "win-x64"),
            Nuget.createAssemblyLayoutWithSubfolder(OsxX64DistributedCacheHost.Service.dll, "osx-x64"),

            // BuildXL.Cache.Host.Configuration
            Nuget.createAssemblyLayout(Net472DistributedCacheHost.Configuration.dll),
            Nuget.createAssemblyLayoutWithSubfolder(WinX64DistributedCacheHost.Configuration.dll, "win-x64"),
            Nuget.createAssemblyLayoutWithSubfolder(OsxX64DistributedCacheHost.Configuration.dll, "osx-x64"),
        ]
    };

    export const interfaces : Deployment.Definition = {
        contents: [
            // ContentStore.Interfaces
            Nuget.createAssemblyLayout(Net451ContentStore.Interfaces.dll),
            Nuget.createAssemblyLayout(Net472ContentStore.Interfaces.dll),
            Nuget.createAssemblyLayoutWithSubfolder(WinX64ContentStore.Interfaces.dll, "win-x64"),
            Nuget.createAssemblyLayoutWithSubfolder(OsxX64ContentStore.Interfaces.dll, "osx-x64"),
            Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.ContentStore").Interfaces.withQualifier(
                { configuration: qualifier.configuration, targetFramework: "netstandard2.0", targetRuntime: "win-x64" }
            ).dll),

            // MemoizationStore.Interfaces
            Nuget.createAssemblyLayout(Net451MemoizationStore.Interfaces.dll),
            Nuget.createAssemblyLayout(Net472MemoizationStore.Interfaces.dll),
            Nuget.createAssemblyLayoutWithSubfolder(WinX64MemoizationStore.Interfaces.dll, "win-x64"),
            Nuget.createAssemblyLayoutWithSubfolder(OsxX64MemoizationStore.Interfaces.dll, "osx-x64"),
        ]
    };

    export const hashing : Deployment.Definition = {
        contents: [
            // ContentStore.Hashing
            Nuget.createAssemblyLayout(Net451ContentStore.Hashing.dll),
            Nuget.createAssemblyLayout(Net472ContentStore.Hashing.dll),
            Nuget.createAssemblyLayoutWithSubfolder(WinX64ContentStore.Hashing.dll, "win-x64"),
            Nuget.createAssemblyLayoutWithSubfolder(OsxX64ContentStore.Hashing.dll, "osx-x64"),
            Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.ContentStore").Hashing.withQualifier(
                { configuration: qualifier.configuration, targetFramework: "netstandard2.0", targetRuntime: "win-x64" }
            ).dll),

            // ContentStore.UtilitiesCore
            Nuget.createAssemblyLayout(Net451ContentStore.UtilitiesCore.dll),
            Nuget.createAssemblyLayout(Net472ContentStore.UtilitiesCore.dll),
            Nuget.createAssemblyLayoutWithSubfolder(WinX64ContentStore.UtilitiesCore.dll, "win-x64"),
            Nuget.createAssemblyLayoutWithSubfolder(OsxX64ContentStore.UtilitiesCore.dll, "osx-x64"),
            Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.withQualifier(
                { configuration: qualifier.configuration, targetFramework: "netstandard2.0", targetRuntime: "win-x64" }
            ).dll),
        ]
    };
}