// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";

namespace Tests {
    export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet472;

    const deployment : Deployment.Definition = {
        contents: qualifier.targetFramework === "net472" ? [
            importFrom("BuildXL.Tools").DistributedBuildRunner.exe,
            importFrom("BuildXL.Tools").VerifyFileContentTable.exe,
        ] : []
    };

    @@public
    export const deployed = (qualifier.targetFramework === "net472") && BuildXLSdk.DeploymentHelpers.deploy({
        definition: deployment, 
        targetLocation: r`tests/${qualifier.configuration}`
    });

    @@public
    export const linuxTestsDeployed = (qualifier.targetRuntime === "osx-x64") && BuildXLSdk.DeploymentHelpers.deploy({
        definition: Tests.Linux.deployment,
        targetLocation: r`linux-x64-tests/${qualifier.configuration}`
    });
}