// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as Nuget from "Sdk.Managed.Tools.NuGet";

namespace Cache {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    /** We copy the sdk's for now. In the future the sdks can contain compiled helpers */
    @@public
    export const deployment : Deployment.Definition = {
        contents: [
            {
                subfolder: r`ContentStore`,
                contents: [
                    importFrom("BuildXL.Cache.ContentStore").Default.deployment,
                ]
            },
            {
                subfolder: r`MemoizationStore`,
                contents: [
                    importFrom("BuildXL.Cache.MemoizationStore").Default.deployment,
                ]
            },
            {
                subfolder: r`Monitor`,
                contents: [
                    importFrom("BuildXL.Cache.Monitor").Default.deployment,
                ]
            },
        ],
    };

    @@public
    export const deployed = BuildXLSdk.DeploymentHelpers.deploy({
        definition: deployment,
        targetLocation: r`${qualifier.configuration}/cache/${qualifier.targetFramework}/${qualifier.targetRuntime}`,
        deploymentOptions: {
            excludedDeployableItems: [
                importFrom("Newtonsoft.Json.v10").pkg,
            ]
        }
    });
}