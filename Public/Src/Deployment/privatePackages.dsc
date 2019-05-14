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

    const cloudBuildlibrary = NugetPackages.pack({
        id: "BuildXL.library.forCloudBuild",
        copyContentFiles: true,
        deployment: {
            contents: [
                Nuget.createAssemblyLayout(importFrom("BuildXL.Engine").withQualifier(net472Qualifier).Processes.dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Pips").withQualifier(net472Qualifier).dll),
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
                        DetoursServices.Deployment.definition,
                        importFrom("BuildXL.Utilities").withQualifier(net472Qualifier).Branding.brandingManifest
                    ]
                },
                {
                    subfolder: r`contentFiles/any/any`,
                    contents: [
                        DetoursServices.Deployment.definition,
                        importFrom("BuildXL.Utilities").withQualifier(net472Qualifier).Branding.brandingManifest
                    ]
                }
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
                        ...importFrom("Microsoft.Applications.Telemetry.Desktop").withQualifier(
                            { targetFramework: "net451" }).pkg.runtime,
                    ],
                },
                {
                    subfolder: r`content`,
                    contents: [
                        DetoursServices.Deployment.definition
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