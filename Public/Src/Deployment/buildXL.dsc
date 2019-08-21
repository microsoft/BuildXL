// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";

namespace BuildXL {

    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    /**
     * The main deployment definition
     */
    @@public
    export const deployment : Deployment.Definition = {
        contents: [
            // primary
            importFrom("BuildXL.App").deployment,
            importFrom("BuildXL.App").serverDeployment,

            // analyzers
            importFrom("BuildXL.Tools").Execution.Analyzer.exe,
            importFrom("BuildXL.Tools").BxlScriptAnalyzer.exe,
            importFrom("BuildXL.Cache.VerticalStore").Analyzer.exe,

            ...addIfLazy(qualifier.targetRuntime !== "osx-x64", () => [
                importFrom("BuildXL.Tools").SandboxedProcessExecutor.exe,
            ]),

            // tools
            ...addIfLazy(qualifier.targetRuntime !== "osx-x64", () => [{
                subfolder: r`tools`,
                contents: [
                    ...(BuildXLSdk.Flags.excludeBuildXLExplorer
                        ? []
                        : [ {
                                subfolder: r`bxp`,
                                contents: [
                                    importFrom("BuildXL.Explorer").App.app.appFolder
                                ]
                            } ] ),
                    ...(BuildXLSdk.Flags.genVSSolution || BuildXLSdk.Flags.excludeBuildXLExplorer
                        ? []
                        : [ {
                                subfolder: r`bxp-server`,
                                contents: [
                                    importFrom("BuildXL.Explorer").Server.withQualifier(
                                        Object.merge<BuildXLSdk.NetCoreAppQualifier>(qualifier, {targetFramework: "netcoreapp3.0"})
                                    ).exe
                                ]
                            } ] ),
                    importFrom("BuildXL.Tools").MsBuildGraphBuilder.deployment,
                    {
                        subfolder: r`bvfs`,
                        contents: qualifier.targetRuntime !== "win-x64" ? [] : [
                            // If the current qualifier is full framework, this tool has to be built with 472
                            importFrom("BuildXL.Cache.ContentStore").VfsApplication.withQualifier({
                                configuration: qualifier.configuration,
                                targetFramework: "net472",
                                targetRuntime: "win-x64"
                            }).exe,
                            importFrom("BuildXL.Cache.ContentStore").App.withQualifier({
                                configuration: qualifier.configuration,
                                targetFramework: "net472",
                                targetRuntime: "win-x64"
                            }).exe
                        ]
                    },
                    {
                        subfolder: r`CMakeNinja`,
                        contents: [
                            importFrom("BuildXL.Tools").CMakeRunner.exe,
                            importFrom("BuildXL.Tools").NinjaGraphBuilder.exe,
                            importFrom("BuildXL.Tools.Ninjson").pkg.contents
                        ]
                    },
                ]
            }])
        ]
    };

    const frameworkSpecificPart = BuildXLSdk.isDotNetCoreBuild
        ? qualifier.targetRuntime
        : qualifier.targetFramework;

    @@public
    export const deployed = BuildXLSdk.DeploymentHelpers.deploy({
        definition: deployment,
        targetLocation: r`${qualifier.configuration}/${frameworkSpecificPart}`,
    });
}