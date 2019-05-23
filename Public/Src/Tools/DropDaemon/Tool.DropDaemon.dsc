// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Managed from "Sdk.Managed";
import * as Deployment from "Sdk.Deployment";
import { NetFx } from "Sdk.BuildXL";

export namespace DropDaemon {
    @@public
    export const exe = !BuildXLSdk.isDropToolingEnabled ? undefined : BuildXLSdk.executable({
        assemblyName: "DropDaemon",
        rootNamespace: "Tool.DropDaemon",
        appConfig: f`DropDaemon.exe.config`,
        sources: globR(d`.`, "*.cs"),
        embeddedResources: [
            {
                resX: f`Statistics.resx`,
            }
        ],
        references: [
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,

            importFrom("ArtifactServices.App.Shared").pkg,
            importFrom("ArtifactServices.App.Shared.Cache").pkg,
            importFrom("Drop.App.Core").pkg,
            importFrom("Drop.Client").pkg,
            importFrom("Drop.RemotableClient.Interfaces").pkg,
            importFrom("ItemStore.Shared").pkg,
            importFrom("Microsoft.ApplicationInsights").pkg,
            importFrom("Microsoft.AspNet.WebApi.Client").pkg,
            importFrom("Microsoft.Diagnostics.Tracing.TraceEvent").pkg,
            importFrom("Microsoft.IdentityModel.Clients.ActiveDirectory").pkg,
            ...BuildXLSdk.visualStudioServicesArtifactServicesSharedPkg,
            importFrom("Microsoft.VisualStudio.Services.BlobStore.Client").pkg,
            importFrom("Microsoft.VisualStudio.Services.Client").pkg,
            importFrom("Microsoft.VisualStudio.Services.InteractiveClient").pkg,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("WindowsAzure.Storage").pkg,
        ],
        internalsVisibleTo: [
            "Test.Tool.DropDaemon",
        ]
    });

    @@public
    export const tool = !BuildXLSdk.isDropToolingEnabled ? undefined : BuildXLSdk.deployManagedTool({
        tool: exe,
        options: toolTemplate,
    });

    @@public
    export const deployment: Deployment.Definition = !BuildXLSdk.isDropToolingEnabled ? undefined : {
        contents: [
            f`Tool.DropDaemonRunner.dsc`,
            f`Tool.DropDaemonRunnerOfficeShim.dsc`,
            f`Tool.DropDaemonInterfaces.dsc`,
            {file: f`LiteralFiles/package.dsc.literal`, targetFileName: a`package.dsc`},
            {file: f`LiteralFiles/package.config.dsc.literal`, targetFileName: a`package.config.dsc`},
            {
                file: f`LiteralFiles/Tool.DropDaemonTool.dsc.literal`,
                targetFileName: a`Tool.DropDaemonTool.dsc`,
            },
            {
                subfolder: "bin",
                contents: [
                    exe,
                ],
            },
        ],
    };
}
