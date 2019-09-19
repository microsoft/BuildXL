// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";
import * as Branding from "BuildXL.Branding";
import * as DetoursServices from "BuildXL.Sandbox.Windows";
import * as Managed from "Sdk.Managed";
import * as Nuget from "Sdk.Managed.Tools.NuGet";

namespace PrivatePackages {
    export declare const qualifier : {
        configuration: "debug" | "release",
        targetRuntime: "win-x64"
    };

    const net451Qualifier : BuildXLSdk.DefaultQualifierWithNet451 = { configuration: qualifier.configuration, targetFramework: "net451", targetRuntime: "win-x64" };
    const net472Qualifier : BuildXLSdk.DefaultQualifier = { configuration: qualifier.configuration, targetFramework: "net472", targetRuntime: "win-x64" };
    const winx64Qualifier : BuildXLSdk.DefaultQualifier = { configuration: qualifier.configuration, targetFramework: "netcoreapp3.0", targetRuntime: "win-x64" };
    const osxx64Qualifier : BuildXLSdk.DefaultQualifier = { configuration: qualifier.configuration, targetFramework: "netcoreapp3.0", targetRuntime: "osx-x64" };

    const cloudBuildlibrary = NugetPackages.pack({
        id: "BuildXL.library.forCloudBuild",
        copyContentFiles: true,
        deployment: {
            contents: [
                // Net 472
                Nuget.createAssemblyLayout(importFrom("BuildXL.Engine").withQualifier(net472Qualifier).Processes.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Pips").withQualifier(net472Qualifier).dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.VerticalStore").withQualifier(net472Qualifier).InMemory.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.VerticalStore").withQualifier(net472Qualifier).Interfaces.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.VerticalStore").withQualifier(net472Qualifier).BasicFilesystem.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.VerticalStore").withQualifier(net472Qualifier).BuildCacheAdapter.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.VerticalStore").withQualifier(net472Qualifier).MemoizationStoreAdapter.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.VerticalStore").withQualifier(net472Qualifier).VerticalAggregator.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities.Instrumentation").withQualifier(net472Qualifier).Common.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities.Instrumentation").withQualifier(net472Qualifier).Tracing.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").withQualifier(net472Qualifier).Collections.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").withQualifier(net472Qualifier).Configuration.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").withQualifier(net472Qualifier).dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").withQualifier(net472Qualifier).Branding.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").withQualifier(net472Qualifier).Interop.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").withQualifier(net472Qualifier).Ipc.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").withQualifier(net472Qualifier).KeyValueStore.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").withQualifier(net472Qualifier).Native.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").withQualifier(net472Qualifier).Storage.dll),

                ...importFrom("RuntimeContracts").withQualifier({ targetFramework: "netstandard2.0" }).pkg.runtime,

                {
                    subfolder: r`content`,
                    contents: [
                        DetoursServices.Deployment.detours,
                        DetoursServices.Deployment.natives,
                        importFrom("BuildXL.Utilities").withQualifier(net472Qualifier).Branding.brandingManifest
                    ]
                },
                {
                    subfolder: r`contentFiles/any/any`,
                    contents: [
                        DetoursServices.Deployment.detours,
                        DetoursServices.Deployment.natives,
                        importFrom("BuildXL.Utilities").withQualifier(net472Qualifier).Branding.brandingManifest
                    ]
                },
                
                // Net Core App 3.0
                Nuget.createAssemblyLayout(importFrom("BuildXL.Engine").withQualifier(winx64Qualifier).Processes.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Pips").withQualifier(winx64Qualifier).dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.VerticalStore").withQualifier(winx64Qualifier).InMemory.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.VerticalStore").withQualifier(winx64Qualifier).Interfaces.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.VerticalStore").withQualifier(winx64Qualifier).BasicFilesystem.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.VerticalStore").withQualifier(winx64Qualifier).BuildCacheAdapter.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.VerticalStore").withQualifier(winx64Qualifier).MemoizationStoreAdapter.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.VerticalStore").withQualifier(winx64Qualifier).VerticalAggregator.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities.Instrumentation").withQualifier(winx64Qualifier).Common.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities.Instrumentation").withQualifier(winx64Qualifier).Tracing.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").withQualifier(winx64Qualifier).Collections.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").withQualifier(winx64Qualifier).Configuration.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").withQualifier(winx64Qualifier).dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").withQualifier(winx64Qualifier).Branding.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").withQualifier(winx64Qualifier).Interop.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").withQualifier(winx64Qualifier).Ipc.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").withQualifier(winx64Qualifier).KeyValueStore.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").withQualifier(winx64Qualifier).Native.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").withQualifier(winx64Qualifier).Storage.dll),

                ...importFrom("RuntimeContracts").withQualifier({ targetFramework: "netcoreapp3.0" }).pkg.runtime
            ]
        },
    });

    const azDevOpslibrary = NugetPackages.pack({
        id: "BuildXL.library.forAzDev",
        deployment: {
            contents: [
                {
                    subfolder: r`lib/net451`,
                    contents: [
                        importFrom("BuildXL.Utilities").withQualifier(net451Qualifier).dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(net451Qualifier).Collections.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(net451Qualifier).Configuration.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(net451Qualifier).Native.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(net451Qualifier).Interop.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(net451Qualifier).System.FormattableString.dll.runtime,
                        importFrom("BuildXL.Utilities.Instrumentation").withQualifier(net451Qualifier).Common.dll.runtime,
                        ...importFrom("Microsoft.Diagnostics.Tracing.EventSource.Redist").withQualifier(
                            { targetFramework: "net451" }).pkg.runtime,
                    ],
                },
                {
                    subfolder: r`runtimes/win-x64/lib/netcoreapp3.0`,
                    contents: [
                        importFrom("BuildXL.Utilities").withQualifier(winx64Qualifier).dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(winx64Qualifier).Collections.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(winx64Qualifier).Configuration.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(winx64Qualifier).Native.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(winx64Qualifier).Interop.dll.runtime,
                        importFrom("BuildXL.Utilities.Instrumentation").withQualifier(winx64Qualifier).Common.dll.runtime,
                    ],
                },
                {
                    subfolder: r`runtimes/osx-x64/lib/netcoreapp3.0/`,
                    contents: [
                        importFrom("BuildXL.Utilities").withQualifier(osxx64Qualifier).dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(osxx64Qualifier).Collections.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(osxx64Qualifier).Configuration.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(osxx64Qualifier).Native.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(osxx64Qualifier).Interop.dll.runtime,
                        importFrom("BuildXL.Utilities.Instrumentation").withQualifier(osxx64Qualifier).Common.dll.runtime,
                    ],
                },
                {
                    subfolder: r`content`,
                    contents: [
                        DetoursServices.Deployment.detours,
                        DetoursServices.Deployment.natives
                    ]
                }
            ]
        }
    });

    @@public
    export const deployment : Deployment.Definition = {
        contents: [
            cloudBuildlibrary,
            azDevOpslibrary
        ]
    };

    @@public
    export const deployed = BuildXLSdk.DeploymentHelpers.deploy({
        definition: deployment,
        targetLocation: r`${qualifier.configuration}/private/pkgs`,
    });
}