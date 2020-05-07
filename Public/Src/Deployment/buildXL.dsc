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
            importFrom("BuildXL.Tools").BxlPipGraphFragmentGenerator.exe,
            importFrom("BuildXL.Cache.VerticalStore").Analyzer.exe,

            importFrom("BuildXL.Tools").SandboxedProcessExecutor.exe,
            ...addIfLazy(qualifier.targetRuntime === "win-x64", () => [
                importFrom("BuildXL.Cache.ContentStore").VfsApplication.exe,
            ]),

            // tools
            ...addIfLazy(qualifier.targetRuntime === "win-x64", () => [{
                subfolder: r`tools`,
                contents: [
                    // Temporarily excluding the viewer since we are reaching the nuget limit size
                    // ...addIf(!BuildXLSdk.Flags.genVSSolution && !BuildXLSdk.Flags.excludeBuildXLExplorer,
                    //     {
                    //         subfolder: r`bxp-server`,
                    //         contents: [
                    //             importFrom("BuildXL.Explorer").Server.withQualifier({targetFramework: "netcoreapp3.1"}).exe
                    //         ]
                    //     }
                    // ),
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

    @@public
    export const deployed = BuildXLSdk.DeploymentHelpers.deploy({
        definition: deployment,
        targetLocation: r`${qualifier.configuration}/${qualifier.targetRuntime}`,
    });
}