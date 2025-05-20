// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as BuildXLSdk from "Sdk.BuildXL";

namespace Cache {
    export declare const qualifier: BuildXLSdk.DefaultQualifierWithNet472;

    /** We copy the sdk's for now. In the future the sdks can contain compiled helpers */
    const deployment : Deployment.Definition = {
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
                subfolder: r`DeployServer`,
                contents: [
                    ...addIfLazy(BuildXLSdk.isDotNetCoreOrStandard,
                        () => [importFrom("BuildXL.Cache.DistributedCache.Host").LauncherServer.exe]
                    ),
                ]
            },
            {
                subfolder: r`BlobLifetimeManager`,
                contents: [
                    ...addIfLazy(qualifier.targetFramework === BuildXLSdk.TargetFrameworks.DefaultTargetFramework,
                        () => [importFrom("BuildXL.Cache.BlobLifetimeManager").Default.deployment]
                    ),
                ]
            },
        ],
    };

    @@public
    export const deployed = BuildXLSdk.DeploymentHelpers.deploy({
        definition: deployment,
        targetLocation: r`${qualifier.configuration}/cache/${qualifier.targetFramework}/${qualifier.targetRuntime}`,
        omitFromDrop: true,
    });
}