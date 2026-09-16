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
            // The ninjson package ships a binary per target runtime (win-x64, linux-x64). Deploy only the
            // one matching the target runtime instead of all of them. There is no macOS build of ninjson
            // (see the 'osSkip' on the package in config.dsc), so nothing is deployed for osx-x64.
            // CODESYNC: the layout below must match NinjsonPath in Public/Src/Tools/Tool.NinjaGraphBuilder/Program.cs
            ...addIfLazy(!BuildXLSdk.isTargetRuntimeOsx, () => [
                {
                    subfolder: r`tools/Ninjson/${qualifier.targetRuntime}`,
                    contents: [
                        Deployment.createFromFilteredStaticDirectory(
                            importFrom("BuildXL.Tools.Ninjson").pkg.contents,
                            r`${qualifier.targetRuntime}`)
                    ]
                }
            ])
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
     * The component detection tool and the Python runtime it needs are Windows binaries, so they are only
     * deployed when the target runtime is win-x64. Including them in the linux-x64/osx-x64 deployments
     * would add ~140MB of unusable content.
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
            ...addIfLazy(BuildXLSdk.Flags.isMicrosoftInternal && BuildXLSdk.isHostOsWin && BuildXLSdk.isTargetRuntimeWin, () => [
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
        targetLocation: (qualifier.targetFramework === Managed.TargetFrameworks.DefaultTargetFramework) // If targetFramework is not a default one (net10.0), then we put it in a separate directory.
        ? r`${qualifier.configuration}/${qualifier.targetRuntime}`
        : r`${qualifier.configuration}/${qualifier.targetFramework}/${qualifier.targetRuntime}`,
    });
}
