// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as Managed from "Sdk.Managed";

namespace TestGeneratorDeployment {
    export declare const qualifier: {configuration: "debug" | "release"};

    @@public
    export const deployment = <Deployment.Definition>{
        contents: [
            ...addIfLazy(Context.getCurrentHost().os === "win", () => [{
                subfolder: a`Win`,
                contents: $.withQualifier({
                    configuration: qualifier.configuration,
                    targetFramework: "net472",
                    targetRuntime: "win-x64"
                }).TestGenerator.deploymentContents
            }]),
            {
                subfolder: a`MacOs`,
                contents: $.withQualifier({
                    configuration: qualifier.configuration,
                    targetFramework: "netcoreapp3.0",
                    targetRuntime: "osx-x64"
                }).TestGenerator.deploymentContents
            }
        ]
    };

    @@public
    export const contents: StaticDirectory = Deployment.deployToDisk({
        definition: deployment,
        targetDirectory: Context.getNewOutputDirectory("TestGenerator")
    }).contents;
}

namespace TestGenerator {
    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "TestGenerator",
        rootNamespace: "BuildXL.FrontEnd.Script.Testing.TestGenerator",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("BuildXL.Utilities").CodeGenerationHelper.dll,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            importFrom("BuildXL.FrontEnd").TypeScript.Net.dll,
        ],
    });

    @@public
    export const deploymentContents: Deployment.DeployableItem[] = [
        exe,
        Helper.dll,
    ];
}
