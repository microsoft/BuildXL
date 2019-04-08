// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";

namespace Tests {
    export declare const qualifier : BuildXLSdk.DefaultQualifier;

    @@public
    export const deployment : Deployment.Definition = {
        contents: [
            ...(qualifier.targetFramework === "net472" ? [
                importFrom("BuildXL.Tools").DistributedBuildRunner.exe,
                importFrom("BuildXL.Tools").VerifyFileContentTable.exe,
            ] : []),
            {
                subfolder: a`osx-x64`,
                contents: qualifier.targetRuntime === "osx-x64" ? [Tests.Osx.deployment] : []
            }
        ]
    };

    @@public
    export const deployed = BuildXLSdk.DeploymentHelpers.deploy({
        definition: deployment, 
        targetLocation: r`tests/${qualifier.configuration}`, 
    });
}