// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";
import * as Gvfs from "BuildXL.CloudTest.Gvfs";

namespace GvfsDeployment {
    export declare const qualifier : Gvfs.GvfsTestQualifer;

    const deployment : Deployment.Definition = {
        contents: [
            f`BuildXL.CloudTest.TestMap.xml`,
            { 
                subfolder: "Gvfs",
                contents: [
                    Gvfs.dll,
                ]
            },
            {
                subfolder: "GvfsInstallers",
                contents: [
                    importFrom("GVFS.Installers").pkg.contents
                ]
            }
        ] 
    };

    @@public
    export const deployed = BuildXLSdk.DeploymentHelpers.deploy({
        definition: deployment, 
        targetLocation: r`CloudTest/${qualifier.configuration}`,
        deploymentOptions: {
            tags: ["cloudTest"]
        },
    });
}