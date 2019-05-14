// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";

namespace Ide {
    export declare const qualifier : { configuration: "debug" | "release"};

    @@public
    export const deployment : Deployment.Definition = {
        contents: [
            ...addIfLazy(Context.getCurrentHost().os === "win", () => [{
                file: importFrom("BuildXL.Ide.VsIntegration").withQualifier({
                    configuration: qualifier.configuration,
                    targetFramework: "net472",
                    targetRuntime: "win-x64"}
                    ).BuildXLVsPackage.vsix,
                targetFileName: a`BuildXL.vs.vsix`,
            },
            {
                file: importFrom("BuildXL.Ide").withQualifier({
                    configuration: qualifier.configuration,
                    targetFramework:"net472",
                    targetRuntime: "win-x64"}
                    ).LanguageService.Server.vsix,
                targetFileName: a`BuildXL.vscode.win.vsix`,
            }]),
            {
                file: importFrom("BuildXL.Ide").withQualifier({
                    configuration: qualifier.configuration,
                    targetFramework:"netcoreapp3.0",
                    targetRuntime: "osx-x64"}
                    ).LanguageService.Server.vsix,
                targetFileName: a`BuildXL.vscode.osx.vsix`,
            }
        ],
    };

    @@public
    export const deployed = BuildXLSdk.DeploymentHelpers.deploy({
        definition: deployment,
        targetLocation: r`${qualifier.configuration}/ide`,
    });
}