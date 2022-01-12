// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as DetoursServices from "BuildXL.Sandbox.Windows";
import * as MacServices from "BuildXL.Sandbox.MacOS";

namespace Tools {

    export declare const qualifier : { configuration: "debug" | "release"};

    namespace Helpers {
        export declare const qualifier: BuildXLSdk.NetCoreAppQualifier;

        export function getTargetLocation(toolName: string) : RelativePath {
            if (qualifier.targetFramework === "netcoreapp3.1") {
                // Keeping the old behavior for backward compatibility of existing scripts relying on the layout.
                return r`${qualifier.configuration}/tools/${toolName}/${qualifier.targetRuntime}`;
            }

            return r`${qualifier.targetFramework}/${qualifier.configuration}/tools/${toolName}/${qualifier.targetRuntime}`;
        }
    }

    namespace SandboxExec {

        export declare const qualifier: BuildXLSdk.NetCoreAppQualifier;

        export const deployment : Deployment.Definition = {
            contents: [
                importFrom("BuildXL.Tools").SandboxExec.exe
            ]
        };

        const deployed = BuildXLSdk.DeploymentHelpers.deploy({
            definition: deployment,
            targetLocation: Helpers.getTargetLocation("SandboxExec"),
        });
    }

    namespace Orchestrator {

        export declare const qualifier: BuildXLSdk.NetCoreAppQualifier;

        export const deployment : Deployment.Definition = {
            contents: [
                importFrom("BuildXL.Tools").withQualifier({
                    targetFramework: qualifier.targetFramework,
                }).Orchestrator.exe
            ],
        };

        const deployed = BuildXLSdk.DeploymentHelpers.deploy({
            definition: deployment,
            targetLocation: Helpers.getTargetLocation("Orchestrator"),
        });
    }

    namespace CoreDumpTester {

        export declare const qualifier: BuildXLSdk.NetCoreAppQualifier;

        export const deployment : Deployment.Definition = {
            contents: [
                MacServices.Deployment.interopLibrary,
                MacServices.Deployment.coreDumpTester
            ],
        };

        // Only used for internal macOS runtime publishing
        const deployed = !BuildXLSdk.Flags.isMicrosoftInternal ? undefined : BuildXLSdk.DeploymentHelpers.deploy({
            definition: deployment,
            targetLocation: Helpers.getTargetLocation("CoreDumpTester"),
        });
    }

    namespace Bvfs
    {
        export declare const qualifier: {
            configuration: "debug" | "release",
            targetFramework: "net472",
            targetRuntime: "win-x64"
        };

        export const deployment : Deployment.Definition = {
            contents: [
                // If the current qualifier is full framework, this tool has to be built with 472
                importFrom("BuildXL.Cache.ContentStore").VfsApplication.exe,
                importFrom("BuildXL.Cache.ContentStore").App.exe
            ]
        };

        const deployed = BuildXLSdk.DeploymentHelpers.deploy({
            definition: deployment,
            targetLocation: r`${qualifier.configuration}/tools/bvfs`
        });
    }

    namespace DistributedBuildRunner {
        export declare const qualifier: BuildXLSdk.DefaultQualifierWithNet472;

        export const deployment : Deployment.Definition = {
            contents: [
                importFrom("BuildXL.Tools").DistributedBuildRunner.exe,
            ],
        };

        const frameworkSpecificPart = BuildXLSdk.isDotNetCoreBuild
            ? qualifier.targetRuntime
            : qualifier.targetFramework;

        const deployed = BuildXLSdk.DeploymentHelpers.deploy({
            definition: deployment,
            targetLocation: (qualifier.targetFramework === "net5.0" || qualifier.targetFramework === "net6.0")
            ? r`${qualifier.targetFramework}/${qualifier.configuration}/tools/DistributedBuildRunner/${qualifier.targetRuntime}`
            : r`${qualifier.configuration}/tools/DistributedBuildRunner/${frameworkSpecificPart}`
        });
    }
}
