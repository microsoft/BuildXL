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

            // tools
            ...addIfLazy(qualifier.targetRuntime !== "osx-x64", () => [{
                subfolder: r`tools`,
                contents: [
                    {
                        subfolder: r`bxp`,
                        contents: [
                            importFrom("BuildXL.Explorer").App.app.appFolder
                        ]
                    },
                    ...(BuildXLSdk.Flags.genVSSolution
                        ? []
                        : [ {
                                subfolder: r`bxp-server`,
                                    contents: [
                                    importFrom("BuildXL.Explorer").Server.withQualifier(
                                        Object.merge<BuildXLSdk.NetCoreAppQualifier>(qualifier, {targetFramework: "netcoreapp2.2"})
                                    ).exe
                                ]
                            } ] ),
                    {
                        subfolder: r`MsBuildGraphBuilder`,
                        contents: qualifier.targetFramework === "netcoreapp2.2" ? [] : [
                            // If the current qualifier is full framework, this tool has to be built with 472
                            importFrom("BuildXL.Tools").MsBuildGraphBuilder.withQualifier(
                                Object.merge<(typeof qualifier) & {targetFramework: "net472"}>(qualifier, {targetFramework: "net472"})).exe
                        ]
                    },
                    {
                        subfolder: r`NinjaGraphBuilder`,
                        contents: [
                            importFrom("BuildXL.Tools").NinjaGraphBuilder.exe,
                            importFrom("BuildXL.Tools.Ninjson").pkg.contents
                        ]
                    },
                    {
                        subfolder: r`CMakeRunner`,
                        contents: [
                            importFrom("BuildXL.Tools").CMakeRunner.exe,
                        ]
                    },
                    {
                        subfolder: r`SandboxedProcessExecutor`,
                        contents: [
                            importFrom("BuildXL.Tools").SandboxedProcessExecutor.exe,
                        ]
                    },
                    ...addIf(BuildXLSdk.Flags.isMicrosoftInternal && BuildXLSdk.isFullFramework && !BuildXLSdk.isTargetRuntimeOsx,
                        {
                            subfolder: r`VmCommandProxy`,
                            contents: [
                                importFrom("CloudBuild.VmCommandProxy").pkg.contents
                            ]
                        }
                    ),
                ]
            }])
        ]
    };

    const frameworkSpecificPart = qualifier.targetFramework === "netcoreapp2.2"
        ? qualifier.targetRuntime
        : qualifier.targetFramework;

    @@public
    export const deployed = BuildXLSdk.DeploymentHelpers.deploy({
        definition: deployment,
        targetLocation: r`${qualifier.configuration}/${frameworkSpecificPart}`,
    });
}