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

    const net472Qualifier : BuildXLSdk.DefaultQualifierWithNet472 = { configuration: qualifier.configuration, targetFramework: "net472", targetRuntime: "win-x64" };
    const winx64Qualifier : BuildXLSdk.DefaultQualifierWithNet472 = { configuration: qualifier.configuration, targetFramework: "netcoreapp3.1", targetRuntime: "win-x64" };
    const osxx64Qualifier : BuildXLSdk.DefaultQualifierWithNet472 = { configuration: qualifier.configuration, targetFramework: "netcoreapp3.1", targetRuntime: "osx-x64" };

    // This package should be deprecated.
    // Likely due ot branching and timing this package has not yet been removed from usage in cloudbuild
    // we should at a strict minimum reduce the contents of this package to just waht is used by the BuildXL.Cache.Props file in the cloudbuid repo
    // Ideally these files become part of the cache package that JuanCarlos has on his plate to clean up.
    const cloudBuildlibrary = NugetPackages.pack({
        id: "BuildXL.library.forCloudBuild",
        copyContentFiles: true,
        deployment: {
            contents: [
                // Net 472
                Nuget.createAssemblyLayout(importFrom("BuildXL.Engine").withQualifier(net472Qualifier).Processes.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Pips").withQualifier(net472Qualifier).dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.VerticalStore").withQualifier(net472Qualifier).ImplementationSupport.dll),
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
                Nuget.createAssemblyLayout(importFrom("BuildXL.Tools").withQualifier(net472Qualifier).VBCSCompilerLogger.dll),

                ...importFrom("RuntimeContracts").withQualifier({ targetFramework: "netstandard2.0" }).pkg.runtime,

                {
                    subfolder: r`content`,
                    contents: [
                        DetoursServices.Deployment.detours,
                        DetoursServices.Deployment.natives,
                        importFrom("BuildXL.Utilities").withQualifier(net472Qualifier).Branding.Manifest.file
                    ]
                },
                {
                    subfolder: r`contentFiles/any/any`,
                    contents: [
                        DetoursServices.Deployment.detours,
                        DetoursServices.Deployment.natives,
                        importFrom("BuildXL.Utilities").withQualifier(net472Qualifier).Branding.Manifest.file
                    ]
                },
                
                // Net Core App 3.1
                Nuget.createAssemblyLayout(importFrom("BuildXL.Engine").withQualifier(winx64Qualifier).Processes.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Pips").withQualifier(winx64Qualifier).dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Cache.VerticalStore").withQualifier(winx64Qualifier).ImplementationSupport.dll),
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
                Nuget.createAssemblyLayout(importFrom("BuildXL.Tools").withQualifier(winx64Qualifier).VBCSCompilerLogger.dll),

                ...importFrom("RuntimeContracts").withQualifier({ targetFramework: "netcoreapp3.1" }).pkg.runtime
            ]
        },
    });

    const vbCsCompilerLoggerToolNet472 = NugetPackages.pack({
        id: "BuildXL.VbCsCompilerLogger.Tool.Net472",
        deployment: {
            contents: [
                {
                    subfolder: r`tools`,
                    contents: [
                        importFrom("BuildXL.Tools").withQualifier(net472Qualifier).VBCSCompilerLogger.dll,
                    ]
                }
            ]
        }
    });

    // This package is to be deprecated as soon as AzDev produes new drop packages that no longer declare
    // this package as a public dependency.
    // The packages in this file should have been packages only for private consumption by the particular .forXYZ customer
    // but VSO broke that contract and declared this package as a public dependency instead of redistributing the files they needed
    // We now have a proper supported BduilXL.Utilities nuget package so this should no longer be needed by them and they
    // have been instructed to drop consumptoin of this.
    // Once they have published a new Drop library, AND CloudBuild has ingested that package we should
    //  1. Remove the .forAzDev package from the CLoudBuild Repo (as they should never have had it)
    //  2. Stop producing this nuget package.
    const azDevOpslibrary = NugetPackages.pack({
        id: "BuildXL.library.forAzDev",
        deployment: {
            contents: [
                {
                    subfolder: r`runtimes/win-x64/lib/netcoreapp3.1`,
                    contents: [
                        importFrom("BuildXL.Utilities").withQualifier(winx64Qualifier).dll,
                        importFrom("BuildXL.Utilities").withQualifier(winx64Qualifier).Collections.dll,
                        importFrom("BuildXL.Utilities").withQualifier(winx64Qualifier).Configuration.dll,
                        importFrom("BuildXL.Utilities").withQualifier(winx64Qualifier).Native.dll,
                        importFrom("BuildXL.Utilities").withQualifier(winx64Qualifier).Interop.dll,
                        importFrom("BuildXL.Utilities.Instrumentation").withQualifier(winx64Qualifier).Common.dll,
                    ],
                },
                {
                    subfolder: r`runtimes/win-x64/native/`,
                    contents: [
                        ...importFrom("BuildXL.Utilities").withQualifier(winx64Qualifier).Native.nativeWin,
                    ],
                },
                {
                    subfolder: r`runtimes/osx-x64/lib/netcoreapp3.1/`,
                    contents: [
                        importFrom("BuildXL.Utilities").withQualifier(osxx64Qualifier).dll,
                        importFrom("BuildXL.Utilities").withQualifier(osxx64Qualifier).Collections.dll,
                        importFrom("BuildXL.Utilities").withQualifier(osxx64Qualifier).Configuration.dll,
                        importFrom("BuildXL.Utilities").withQualifier(osxx64Qualifier).Native.dll,
                        importFrom("BuildXL.Utilities").withQualifier(osxx64Qualifier).Interop.dll,
                        importFrom("BuildXL.Utilities.Instrumentation").withQualifier(osxx64Qualifier).Common.dll,
                    ],
                },
                {
                    subfolder: r`runtimes/osx-x64/native/`,
                    contents: [
                        ...importFrom("BuildXL.Utilities").withQualifier(osxx64Qualifier).Native.nativeMac,
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

    const deployment : Deployment.Definition = {
        contents: [
            cloudBuildlibrary,
            vbCsCompilerLoggerToolNet472,
            azDevOpslibrary,
        ]
    };

    @@public
    export const deployed = BuildXLSdk.DeploymentHelpers.deploy({
        definition: deployment,
        targetLocation: r`${qualifier.configuration}/private/pkgs`,
    });
}