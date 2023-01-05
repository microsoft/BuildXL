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
        assemblyBindingRedirects: dropDaemonBindingRedirects(),
        sources: globR(d`.`, "*.cs"),

        references: [
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Authentication.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").Ipc.Providers.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").SBOMUtilities.dll,
            importFrom("BuildXL.Tools").ServicePipDaemon.dll,
            importFrom("ArtifactServices.App.Shared").pkg,
            importFrom("ArtifactServices.App.Shared.Cache").pkg,
            importFrom("Drop.App.Core").pkg,
            importFrom("Drop.Client").pkg,
            importFrom("ItemStore.Shared").pkg,
            importFrom("Microsoft.ApplicationInsights").pkg,
            importFrom("Microsoft.AspNet.WebApi.Client").pkg,
            importFrom("Microsoft.IdentityModel.Clients.ActiveDirectory").pkg,
            importFrom("Microsoft.VisualStudio.Services.ArtifactServices.Shared").pkg,
            importFrom("Microsoft.VisualStudio.Services.BlobStore.Client").pkg,
            importFrom("Microsoft.VisualStudio.Services.Client").pkg,
            importFrom("Microsoft.VisualStudio.Services.InteractiveClient").pkg,
            importFrom("WindowsAzure.Storage").pkg,
            importFrom("Microsoft.Azure.Storage.Common").pkg,
            importFrom("Microsoft.Extensions.Logging.Abstractions.v6.0.0").pkg,

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
        runtimeContentToSkip: dropDaemonRuntimeContentToSkip()
    });

    const temporarySdkDropNextToEngineFolder = d`${Context.getBuildEngineDirectory()}/Sdk/Sdk.Drop/bin`;
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
    export function dropDaemonBindingRedirects() {
        return [
            ...BuildXLSdk.cacheBindingRedirects(),
            {
                name: "Newtonsoft.Json",
                publicKeyToken: "30ad4fe6b2a6aeed",
                culture: "neutral",
                oldVersion: "0.0.0.0-13.0.0.0",
                newVersion: "13.0.0.0", // Corresponds to { id: "Newtonsoft.Json", version: "13.0.1", alias: "Newtonsoft.Json.v13.0.1" }
            },
            {
                name: "System.Text.Json",
                publicKeyToken: "cc7b13ffcd2ddd51",
                culture: "neutral",
                oldVersion: "0.0.0.0-5.0.0.0",
                newVersion: "5.0.0.0",
            },
            {
                name: "System.Text.Encodings.Web",
                publicKeyToken: "cc7b13ffcd2ddd51",
                culture: "neutral",
                oldVersion: "0.0.0.0-5.0.0.1",
                newVersion: "5.0.0.1", // Corresponds to { id: "System.Text.Encodings.Web", version: "4.7.2" },
            }
        ];
    }

    @@public
    export function dropDaemonRuntimeContentToSkip() {
        return [
            importFrom("System.Text.Json").withQualifier({ targetFramework: "netstandard2.0" }).pkg,
            importFrom("System.Text.Encodings.Web").withQualifier({ targetFramework: "netstandard2.0" }).pkg,
            importFrom("Microsoft.Extensions.Logging.Abstractions").pkg,
        ];
    }

    @@public
    export function dropDaemonSbomPackages() {
        // Any package needed for SBOM generation should
        // be specified here, as the test module uses this function
        // to import them for testing the SBOM generation library.
        return [
            importFrom("Microsoft.Parsers.ManifestGenerator").pkg,
            importFrom("Microsoft.SBOMCore").pkg,
            importFrom("Microsoft.Sbom.Contracts").pkg,
            importFrom("Microsoft.Sbom.Extensions").pkg,
            importFrom("Microsoft.Sbom.Parsers.Spdx22SbomParser").pkg,
            importFrom("Microsoft.ComponentDetection.Contracts").pkg,
            importFrom("Microsoft.Sbom.Adapters").pkg,
            importFrom("System.Text.Json.v5.0.0").pkg,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("System.Text.Encodings.Web.v5.0.1").pkg,
        ];
    }


}
