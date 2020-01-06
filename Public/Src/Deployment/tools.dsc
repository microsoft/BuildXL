// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as DetoursServices from "BuildXL.Sandbox.Windows";
import * as MacServices from "BuildXL.Sandbox.MacOS";

namespace Tools {

    export declare const qualifier : { configuration: "debug" | "release"};

    namespace XldbAnalyzer {

        export declare const qualifier: BuildXLSdk.NetCoreAppQualifier;

        export const deployment : Deployment.Definition = {
            contents: [
                importFrom("BuildXL.Tools").Xldb.Analyzer.exe,
            ]
        };
    
        const deployed = BuildXLSdk.DeploymentHelpers.deploy({
            definition: deployment,
            targetLocation: r`${qualifier.configuration}/tools/XldbAnalyzer/${qualifier.targetRuntime}`,
        });
    }

    namespace SandboxExec {

        export declare const qualifier: BuildXLSdk.NetCoreAppQualifier;

        export const deployment : Deployment.Definition = {
            contents: [
                ...addIfLazy(MacServices.Deployment.macBinaryUsage !== "none" && qualifier.targetRuntime === "osx-x64", () => [
                    MacServices.Deployment.kext,
                    MacServices.Deployment.sandboxMonitor,
                    MacServices.Deployment.sandboxLoadScripts
                ]),
                importFrom("BuildXL.Tools").SandboxExec.exe
            ]
        };

        const deployed = BuildXLSdk.DeploymentHelpers.deploy({
            definition: deployment,
            targetLocation: r`${qualifier.configuration}/tools/SandboxExec/${qualifier.targetRuntime}`,
        });
    }

    namespace Orchestrator {

        export declare const qualifier: BuildXLSdk.NetCoreAppQualifier;

        export const deployment : Deployment.Definition = {
            contents: [
                importFrom("BuildXL.Tools").withQualifier({
                    targetFramework: "netcoreapp3.0",
                }).Orchestrator.exe
            ],
        };

        const deployed = BuildXLSdk.DeploymentHelpers.deploy({
            definition: deployment,
            targetLocation: r`${qualifier.configuration}/tools/Orchestrator/${qualifier.targetRuntime}`
        });
    }

    namespace BuildExplorer {
        export declare const qualifier: {
            configuration: "debug" | "release",
            targetRuntime: "win-x64"
        };

        export const deployment : Deployment.Definition = {
            contents: addIf(!BuildXLSdk.Flags.excludeBuildXLExplorer,
                {
                    subfolder: r`app`,
                    contents: [importFrom("BuildXL.Explorer").App.app.appFolder],
                }
            )
        };

        const deployed = !BuildXLSdk.Flags.excludeBuildXLExplorer
            ? BuildXLSdk.DeploymentHelpers.deploy({
                definition: deployment,
                targetLocation: r`${qualifier.configuration}/tools/bxp`
              })
            : undefined;
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
            targetLocation: r`${qualifier.configuration}/tools/DistributedBuildRunner/${frameworkSpecificPart}`
        });
    }
}
