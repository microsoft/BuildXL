// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
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

            {
                subfolder: "tools/NinjaGraphBuilder",
                contents: [ importFrom("BuildXL.Tools").NinjaGraphBuilder.exe ]
            },
            {
                subfolder: "tools/Ninjson",
                contents: [ importFrom("BuildXL.Tools.Ninjson").pkg.contents ]
            }
        ]
    };

    /**
     * Extended deployment for CloudBuild that includes the component governance
     * detection tool and Python runtime alongside the core BuildXL binaries.
     * GenericBuildRunner in CloudBuild invokes ComponentDetector.exe from the
     * ComponentDetection/Tool/ subfolder in the BuildXL tool drop.
     *
     * This is NOT used for NuGet packaging to avoid exceeding package size limits.
     *
     * //codesync: The layout below (tools/ComponentDetection/Tool/ and tools/ComponentDetection/Python/)
     * must match the paths in CloudBuild's BuildXLComponentGovernanceHelper.cs
     * (private/Tools/GenericBuildRunner/shared/BuildXLComponentGovernanceHelper.cs).
     * If the subfolder structure changes here, update ResolveComponentDetectionBaseDir
     * and the path construction in RunPreBuildComponentGovernance accordingly.
     */
    @@public
    export const cloudBuildDeployment : Deployment.Definition = {
        contents: [
            deployment,
            ...addIfLazy(BuildXLSdk.Flags.isMicrosoftInternal && BuildXLSdk.isHostOsWin, () => [
                {
                    subfolder: r`tools/ComponentDetection/Tool`,
                    contents: [
                        Deployment.createFromFilteredStaticDirectory(
                            importFrom("Microsoft.VisualStudio.Services.Governance.ComponentDetection").pkg.contents,
                            r`windows`)
                    ]
                },
                {
                    subfolder: r`tools/ComponentDetection/Python`,
                    contents: [
                        Deployment.createFromFilteredStaticDirectory(
                            importFrom("Python").pkg.contents,
                            r`tools`)
                    ]
                }
            ])
        ]
    };

    @@public
    export const deployed = BuildXLSdk.DeploymentHelpers.deploy({
        definition: cloudBuildDeployment,
        targetLocation: (qualifier.targetFramework === Managed.TargetFrameworks.DefaultTargetFramework) // If targetFramework is not a default one (net9.0), then we put it in a separate directory.
        ? r`${qualifier.configuration}/${qualifier.targetRuntime}`
        : r`${qualifier.configuration}/${qualifier.targetFramework}/${qualifier.targetRuntime}`,
    });
}
