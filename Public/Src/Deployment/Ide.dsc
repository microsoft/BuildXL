// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";

namespace Ide {
    export declare const qualifier : { configuration: "debug" | "release"};

    const deployment : Deployment.Definition = {
        contents: [
            ...addIfLazy(Context.getCurrentHost().os === "win", () => [
                ...addIfLazy(BuildXLSdk.Flags.isMicrosoftInternal, () => [{
                    file: importFrom("BuildXL.Ide.VsIntegration").withQualifier({
                        targetFramework: "net472",
                        targetRuntime: "win-x64"}
                        ).BuildXLVsPackage.vsix,
                    targetFileName: a`BuildXL.vs.vsix`,
                    }
                ]),
                {
                    file: importFrom("BuildXL.Ide").withQualifier({
                        targetFramework: BuildXLSdk.TargetFrameworks.DefaultTargetFramework,
                        targetRuntime: "win-x64"}
                        ).LanguageService.Server.vsix,
                    targetFileName: a`BuildXL.vscode.win.vsix`,
                },
                {
                    file: importFrom("BuildXL.Ide").withQualifier({
                        targetFramework: BuildXLSdk.TargetFrameworks.DefaultTargetFramework,
                        targetRuntime: "osx-x64"}
                        ).LanguageService.Server.vsix,
                    targetFileName: a`BuildXL.vscode.osx.vsix`,
                }
            ]),
            // The Linux extension is built only on Linux instead of cross compiling is so that all Linux artifacts that we publish are built on Linux end to end.
            // While there is nothing Linux specific here (because its all dotnet/js) this follows the same pattern we have used for other Linux specific packages we publish
            ...addIfLazy(Context.getCurrentHost().os === "unix", () => [
                {
                    file: importFrom("BuildXL.Ide").withQualifier({
                        targetFramework: BuildXLSdk.TargetFrameworks.DefaultTargetFramework,
                        targetRuntime: "linux-x64"}
                        ).LanguageService.Server.vsix,
                    targetFileName: a`BuildXL.vscode.linux.vsix`,
                }
            ]),
        ],
    };

    @@public
    export const deployed = BuildXLSdk.DeploymentHelpers.deploy({
        definition: deployment,
        targetLocation: r`${qualifier.configuration}/ide`,
    });
}