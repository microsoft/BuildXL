// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";

namespace PrivatePackages {
    export declare const qualifier : {
        configuration: "debug" | "release",
        targetRuntime: "win-x64"
    };

    const net472Qualifier : BuildXLSdk.DefaultQualifierWithNet472 = { configuration: qualifier.configuration, targetFramework: "net472", targetRuntime: "win-x64" };

    const vbCsCompilerLoggerToolNet472 = NugetPackages.pack({
        id: "BuildXL.VbCsCompilerLogger.Tool.Net472",
        deployment: {
            contents: [
                {
                    subfolder: r`tools`,
                    contents: [
                        importFrom("BuildXL.Tools").withQualifier(net472Qualifier).VBCSCompilerLogger.dll,
                    ]
                }
            ]
        }
    });

    const deployment : Deployment.Definition = {
        contents: [
            vbCsCompilerLoggerToolNet472,
        ]
    };

    @@public
    export const deployed = BuildXLSdk.DeploymentHelpers.deploy({
        definition: deployment,
        targetLocation: r`${qualifier.configuration}/private/pkgs`,
    });
}