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
                    configuration: qualifier.configuration,
                    targetFramework: "netcoreapp3.0",
                    targetRuntime: qualifier.targetRuntime
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
            contents: [
                ...(BuildXLSdk.Flags.excludeBuildXLExplorer
                ? []
                : [{
                    // There is an error when deploying a single sealed directory where the graph
                    // invalidly thinks there is a duplicate deployment between robocopy and the sealed
                    // directory... Using a subfolder is a workaround for now.
                    subfolder: "bxp",
                    contents: [
                        importFrom("BuildXL.Explorer").App.app.appFolder
                    ]
                }])
            ]            
        };
            
        const deployed = BuildXLSdk.DeploymentHelpers.deploy({
            definition: deployment,
            targetLocation: r`${qualifier.configuration}/tools`
        });

    }
}
