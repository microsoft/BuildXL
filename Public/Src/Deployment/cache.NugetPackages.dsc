// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as Nuget from "Sdk.Managed.Tools.NuGet";

namespace Cache.NugetPackages {
    export declare const qualifier : { configuration: "debug" | "release"};

    const Net451ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ configuration: qualifier.configuration, targetFramework: "net451", targetRuntime: "win-x64" });
    const Net461ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ configuration: qualifier.configuration, targetFramework: "net461", targetRuntime: "win-x64" });
    const Net472ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ configuration: qualifier.configuration, targetFramework: "net472", targetRuntime: "win-x64" });
    const WinX64ContentStore = importFrom("BuildXL.Cache.ContentStore").withQualifier({ configuration: qualifier.configuration, targetFramework: "netcoreapp3.0", targetRuntime: "win-x64" });

    const Net451MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ configuration: qualifier.configuration, targetFramework: "net451", targetRuntime: "win-x64" });
    const Net461MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ configuration: qualifier.configuration, targetFramework: "net461", targetRuntime: "win-x64" });
    const Net472MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ configuration: qualifier.configuration, targetFramework: "net472", targetRuntime: "win-x64" });
    const WinX64MemoizationStore = importFrom("BuildXL.Cache.MemoizationStore").withQualifier({ configuration: qualifier.configuration, targetFramework: "netcoreapp3.0", targetRuntime: "win-x64" });

    const Net461DistributedCacheHost = importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ configuration: qualifier.configuration, targetFramework: "net461", targetRuntime: "win-x64" });

    export const tools : Deployment.Definition = {
        contents: [
            {
                subfolder: r`tools`,
                contents: [
                    Net461ContentStore.App.exe,
                    Net461MemoizationStore.App.exe,
                    Net461DistributedCacheHost.Configuration.dll,
                    Net461DistributedCacheHost.Service.dll,
                ]
            },
        ]
    };

    export const libraries : Deployment.Definition = {
        contents: [
            // ContentStore.Distributed
            Nuget.createAssemblyLayout(Net451ContentStore.Distributed.dll),
            Nuget.createAssemblyLayout(Net461ContentStore.Distributed.dll),
            Nuget.createAssemblyLayout(Net472ContentStore.Distributed.dll),
            Nuget.createAssemblyLayout(WinX64ContentStore.Distributed.dll),
            // ContentStore.Library
            Nuget.createAssemblyLayout(Net451ContentStore.Library.dll),
            Nuget.createAssemblyLayout(Net461ContentStore.Library.dll),
            Nuget.createAssemblyLayout(Net472ContentStore.Library.dll),
            Nuget.createAssemblyLayout(WinX64ContentStore.Library.dll),
            // ContentStore.Grpc
            Nuget.createAssemblyLayout(Net451ContentStore.Grpc.dll),
            Nuget.createAssemblyLayout(Net461ContentStore.Grpc.dll),
            Nuget.createAssemblyLayout(Net472ContentStore.Grpc.dll),
            Nuget.createAssemblyLayout(WinX64ContentStore.Grpc.dll),

            // ContentStore.Vsts
            ...addIfLazy(BuildXLSdk.Flags.isVstsArtifactsEnabled, () => [
                Nuget.createAssemblyLayout(Net451ContentStore.Vsts.dll),
                Nuget.createAssemblyLayout(Net461ContentStore.Vsts.dll),
                Nuget.createAssemblyLayout(Net472ContentStore.Vsts.dll)
            ]),

            // ContentStore.VstsInterfaces
            Nuget.createAssemblyLayout(Net451ContentStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayout(Net461ContentStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayout(Net472ContentStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayout(WinX64ContentStore.VstsInterfaces.dll),

            // MemoizationStore.Distributed
            Nuget.createAssemblyLayout(Net451MemoizationStore.Distributed.dll),
            Nuget.createAssemblyLayout(Net461MemoizationStore.Distributed.dll),
            Nuget.createAssemblyLayout(Net472MemoizationStore.Distributed.dll),
            Nuget.createAssemblyLayout(WinX64MemoizationStore.Distributed.dll),
            // MemoizationStore.Library
            Nuget.createAssemblyLayout(Net451MemoizationStore.Library.dll),
            Nuget.createAssemblyLayout(Net461MemoizationStore.Library.dll),
            Nuget.createAssemblyLayout(Net472MemoizationStore.Library.dll),
            Nuget.createAssemblyLayout(WinX64MemoizationStore.Library.dll),

            // MemoizationStore.Vsts
            ...addIfLazy(BuildXLSdk.Flags.isVstsArtifactsEnabled, () => [
                Nuget.createAssemblyLayout(Net451MemoizationStore.Vsts.dll),
                Nuget.createAssemblyLayout(Net461MemoizationStore.Vsts.dll),
                Nuget.createAssemblyLayout(Net472MemoizationStore.Vsts.dll)
            ]),

            // MemoizationStore.VstsInterfaces
            Nuget.createAssemblyLayout(Net451MemoizationStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayout(Net461MemoizationStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayout(Net472MemoizationStore.VstsInterfaces.dll),
            Nuget.createAssemblyLayout(WinX64MemoizationStore.VstsInterfaces.dll),
        ]
    };

    export const interfaces : Deployment.Definition = {
        contents: [
            // ContentStore.Interfaces
            Nuget.createAssemblyLayout(Net451ContentStore.Interfaces.dll),
            Nuget.createAssemblyLayout(Net461ContentStore.Interfaces.dll),
            Nuget.createAssemblyLayout(Net472ContentStore.Interfaces.dll),
            Nuget.createAssemblyLayout(WinX64ContentStore.Interfaces.dll),
            Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.ContentStore").Interfaces.withQualifier(
                { configuration: qualifier.configuration, targetFramework: "netstandard2.0", targetRuntime: "win-x64" }
            ).dll),

            // MemoizationStore.Interfaces
            Nuget.createAssemblyLayout(Net451MemoizationStore.Interfaces.dll),
            Nuget.createAssemblyLayout(Net461MemoizationStore.Interfaces.dll),
            Nuget.createAssemblyLayout(Net472MemoizationStore.Interfaces.dll),
            Nuget.createAssemblyLayout(WinX64MemoizationStore.Interfaces.dll),
        ]
    };

    export const hashing : Deployment.Definition = {
        contents: [
            // ContentStore.Hashing
            Nuget.createAssemblyLayout(Net451ContentStore.Hashing.dll),
            Nuget.createAssemblyLayout(Net461ContentStore.Hashing.dll),
            Nuget.createAssemblyLayout(Net472ContentStore.Hashing.dll),
            Nuget.createAssemblyLayout(WinX64ContentStore.Hashing.dll),
            Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.ContentStore").Hashing.withQualifier(
                { configuration: qualifier.configuration, targetFramework: "netstandard2.0", targetRuntime: "win-x64" }
            ).dll),

            // ContentStore.UtilitiesCore
            Nuget.createAssemblyLayout(Net451ContentStore.UtilitiesCore.dll),
            Nuget.createAssemblyLayout(Net461ContentStore.UtilitiesCore.dll),
            Nuget.createAssemblyLayout(Net472ContentStore.UtilitiesCore.dll),
            Nuget.createAssemblyLayout(WinX64ContentStore.UtilitiesCore.dll),
            Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.withQualifier(
                { configuration: qualifier.configuration, targetFramework: "netstandard2.0", targetRuntime: "win-x64" }
            ).dll),
        ]
    };
}