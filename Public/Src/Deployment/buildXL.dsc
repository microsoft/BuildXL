// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";

namespace BuildXL {

    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    /** 
     * The notice file compiles the license and copyright information for any code or other materials under open source licenses that we distribute in a Microsoft Offering. 
     * The notice file is automatically generated in our rolling builds before we execute the selfhost build that produce the nuget packages.
     * In those rolling builds, the notice file is put on the source root.
    */
    const noticeFilePath = f`${Context.getMount("SourceRoot").path}/NOTICE.txt`;

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

            // tools
            importFrom("BuildXL.Tools").CMakeRunner.exe,
            importFrom("BuildXL.Tools").NinjaGraphBuilder.exe,
            ...addIfLazy(qualifier.targetRuntime === "win-x64", () => [
                importFrom("BuildXL.Tools.Ninjson").pkg.contents
            ]), 
            ...addIfLazy(qualifier.targetRuntime === "linux-x64", () => [
                importFrom("BuildXL.Tools.Ninjson.linux-x64").pkg.contents
            ]),
            
            ...addIfLazy(File.exists(noticeFilePath), () => [
                noticeFilePath
            ]),            
        ]
    };

    @@public
    export const deployed = BuildXLSdk.DeploymentHelpers.deploy({
        definition: deployment,
        targetLocation: (qualifier.targetFramework === Managed.TargetFrameworks.DefaultTargetFramework) // If targetFramework is not a default one (net6.0), then we put it in a separate directory.
        ? r`${qualifier.configuration}/${qualifier.targetRuntime}`
        : r`${qualifier.configuration}/${qualifier.targetFramework}/${qualifier.targetRuntime}`,
    });
}
