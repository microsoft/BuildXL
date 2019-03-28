// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";
import * as Branding from "BuildXL.Branding";
import * as DetoursServices from "BuildXL.Sandbox.Windows";
import * as Managed from "Sdk.Managed";

namespace PrivatePackages {
    export declare const qualifier : {
        configuration: "debug" | "release"
    };

    const net451Qualifier : BuildXLSdk.DefaultQualifierWithNet451 = { configuration: qualifier.configuration, targetFramework: "net451", targetRuntime: "win-x64" };
    const net461Qualifier : BuildXLSdk.DefaultQualifier = { configuration: qualifier.configuration, targetFramework: "net461", targetRuntime: "win-x64" };
    const net472Qualifier : BuildXLSdk.DefaultQualifier = { configuration: qualifier.configuration, targetFramework: "net472", targetRuntime: "win-x64" };

    const cloudBuildlibrary = NugetPackages.pack({
        id: "BuildXL.library.forCloudBuild",
        deployment: {
            contents: [
                {
                    subfolder: r`lib/${net461Qualifier.targetFramework}`,
                    contents: [
                        importFrom("BuildXL.Engine").withQualifier(net461Qualifier).Processes.dll.runtime,
                        importFrom("BuildXL.Pips").withQualifier(net461Qualifier).dll.runtime,
                        importFrom("BuildXL.Utilities.Instrumentation").withQualifier(net461Qualifier).Common.dll.runtime,
                        importFrom("BuildXL.Utilities.Instrumentation").withQualifier(net461Qualifier).Tracing.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(net461Qualifier).Collections.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(net461Qualifier).Configuration.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(net461Qualifier).dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(net461Qualifier).Branding.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(net461Qualifier).Interop.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(net461Qualifier).Ipc.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(net461Qualifier).KeyValueStore.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(net461Qualifier).Native.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(net461Qualifier).Storage.dll.runtime,
        
                        ...importFrom("RuntimeContracts").withQualifier({ targetFramework: net461Qualifier.targetFramework }).pkg.runtime,
                    ],
                },
                {
                    subfolder: r`lib/${net472Qualifier.targetFramework}`,
                    contents: [
                        importFrom("BuildXL.Engine").withQualifier(net472Qualifier).Processes.dll.runtime,
                        importFrom("BuildXL.Pips").withQualifier(net472Qualifier).dll.runtime,
                        importFrom("BuildXL.Utilities.Instrumentation").withQualifier(net472Qualifier).Common.dll.runtime,
                        importFrom("BuildXL.Utilities.Instrumentation").withQualifier(net472Qualifier).Tracing.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(net472Qualifier).Collections.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(net472Qualifier).Configuration.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(net472Qualifier).dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(net472Qualifier).Branding.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(net472Qualifier).Interop.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(net472Qualifier).Ipc.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(net472Qualifier).KeyValueStore.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(net472Qualifier).Native.dll.runtime,
                        importFrom("BuildXL.Utilities").withQualifier(net472Qualifier).Storage.dll.runtime,
        
                        ...importFrom("RuntimeContracts").withQualifier({ targetFramework: net472Qualifier.targetFramework }).pkg.runtime,
                    ],
                },
                {
                    subfolder: r`content`,
                    contents: [
                        DetoursServices.Deployment.definition,
                        importFrom("BuildXL.Utilities").withQualifier(net461Qualifier).Branding.brandingManifest
                    ]
                }
            ]
        }
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