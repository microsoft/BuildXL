// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Managed from "Sdk.Managed";
import * as Deployment from "Sdk.Deployment";
import { NetFx } from "Sdk.BuildXL";

export namespace DropDaemon {
    @@public 
    export declare const qualifier: BuildXLSdk.Net8Qualifier;

    @@public
    export const exe = !BuildXLSdk.isDropToolingEnabled ? undefined : BuildXLSdk.executable({
        assemblyName: "DropDaemon",
        rootNamespace: "Tool.DropDaemon",
        appConfig: f`DropDaemon.exe.config`,
        assemblyBindingRedirects: dropDaemonBindingRedirects(),
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Authentication.dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").Ipc.Providers.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").SBOMUtilities.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("BuildXL.Utilities").Branding.dll,
            importFrom("BuildXL.Tools").ServicePipDaemon.dll,
            importFrom("ArtifactServices.App.Shared").pkg,
            importFrom("Drop.App.Core").pkg,
            importFrom("Drop.Client").pkg,
            importFrom("ItemStore.Shared").pkg,
            importFrom("Microsoft.ApplicationInsights").pkg,
            importFrom("Microsoft.AspNet.WebApi.Client").pkg,
            importFrom("Microsoft.VisualStudio.Services.ArtifactServices.Shared").pkg,
            importFrom("Microsoft.VisualStudio.Services.BlobStore.Client").pkg,
            importFrom("Microsoft.VisualStudio.Services.Client").pkg,
            importFrom("Microsoft.VisualStudio.Services.InteractiveClient").pkg,
            importFrom("Microsoft.Azure.Storage.Common").pkg,
            importFrom("Microsoft.Extensions.Logging.Abstractions").pkg,

            // We need to reference this even though the codepath which uses the path is never activated
            // because of the way that runtime assemblies are loaded into memory.
            importFrom("Microsoft.VisualStudio.Services.BlobStore.Client.Cache").pkg,
            ...BuildXLSdk.systemThreadingTasksDataflowPackageReference,

            // SBOM related
            ...dropDaemonSbomPackages()
        ],
        internalsVisibleTo: [
            "Test.Tool.DropDaemon",
        ],
        // TODO: The SBOM package expects a netcore 7 JSON package, even for netcore 6. Remove when we stop supporting net6
        deploymentOptions: { ignoredSelfContainedRuntimeFilenames: [a`System.Text.Encodings.Web.dll`, a`System.Text.Json.dll`, a`System.IO.Pipelines.dll`] },
    });

    const temporarySdkDropNextToEngineFolder = d`${Context.getBuildEngineDirectory()}/Sdk/Sdk.Drop/bin`;
    // Temp tool is only used in selfhost builds on Windows, os it's fine to have a hardcoded path here.
    const temporaryDropDaemonTool : Transformer.ToolDefinition = {
        exe: f`${temporarySdkDropNextToEngineFolder}/DropDaemon.exe`,
        runtimeDirectoryDependencies: [
            Transformer.sealSourceDirectory({
                root: temporarySdkDropNextToEngineFolder,
                include: "allDirectories",
            }),
        ],
        untrackedDirectoryScopes: [
            Context.getUserHomeDirectory(),
            d`${Context.getMount("ProgramData").path}`,
        ],
        dependsOnWindowsDirectories: true,
        dependsOnAppDataDirectory: true,
        prepareTempDirectory: true,
    };

    @@public
    export const tool = !BuildXLSdk.isDropToolingEnabled
        ? undefined
        : temporaryDropDaemonTool;
        //: BuildXLSdk.deployManagedTool({
        //    tool: exe,
        //    options: toolTemplate,
        //});

    const specs = [
        f`Tool.DropDaemonRunner.dsc`,
        f`Tool.DropDaemonRunnerOfficeShim.dsc`,
        f`Tool.DropDaemonInterfaces.dsc`,
        f`Tool.DropDaemonCloudBuildHelper.dsc`,
        {file: f`LiteralFiles/package.dsc.literal`, targetFileName: a`package.dsc`},
        {file: f`LiteralFiles/package.config.dsc.literal`, targetFileName: a`package.config.dsc`},
        {
            file: f`LiteralFiles/Tool.DropDaemonTool.dsc.literal`,
            targetFileName: a`Tool.DropDaemonTool.dsc`,
        }];

    @@public
    export const evaluationOnlyDeployment: Deployment.Definition = !BuildXLSdk.isDropToolingEnabled ? undefined : {
        contents: specs
    };

    @@public
    export const deployment: Deployment.Definition = !BuildXLSdk.isDropToolingEnabled ? undefined : {
        contents: [
            ...specs,
            {
                subfolder: "bin",
                contents: [
                    exe,
                ],
            },
        ],
    };

    @@public
    export function selectDeployment(evaluationOnly: boolean) : Deployment.Definition {
        return evaluationOnly? evaluationOnlyDeployment : deployment;
    }

    @@public
    export function dropDaemonBindingRedirects() : Managed.AssemblyBindingRedirect[] {
        return [
            ...BuildXLSdk.cacheBindingRedirects(),
            {
                name: "Newtonsoft.Json",
                publicKeyToken: "30ad4fe6b2a6aeed",
                culture: "neutral",
                oldVersion: "0.0.0.0-13.0.0.0",
                newVersion: "13.0.0.0", // Corresponds to { id: "Newtonsoft.Json", version: "13.0.1" }
            },
            {
                name: "System.Text.Json",
                publicKeyToken: "cc7b13ffcd2ddd51",
                culture: "neutral",
                oldVersion: "0.0.0.0-8.0.0.0",
                newVersion: "8.0.0.0"
            },
            {
                name: "System.Text.Encodings.Web",
                publicKeyToken: "cc7b13ffcd2ddd51",
                culture: "neutral",
                oldVersion: "0.0.0.0-8.0.0.0",
                newVersion: "8.0.0.0"
            }
        ];
    }

    @@public
    export function dropDaemonSbomPackages() : Managed.ManagedNugetPackage[] {
        // Any package needed for SBOM generation should
        // be specified here, as the test module uses this function
        // to import them for testing the SBOM generation library.
        return [
            importFrom("Microsoft.SbomCore").pkg,
            importFrom("Microsoft.Sbom.Common").pkg,
            importFrom("Microsoft.Sbom.Parsers.Spdx22SbomParser").pkg,
            importFrom("Microsoft.Sbom.Contracts").pkg,
            importFrom("Microsoft.Sbom.Extensions").pkg,
            importFrom("Microsoft.Sbom.Parsers.Spdx22SbomParser").pkg,
            importFrom("Microsoft.ComponentDetection.Contracts").pkg,
            importFrom("Microsoft.Sbom.Adapters").pkg,
            importFrom("packageurl-dotnet").pkg,
            importFrom("System.Text.Json").pkg,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("System.Text.Encodings.Web").pkg,
            importFrom("Serilog").pkg,
        ];
    }


}
