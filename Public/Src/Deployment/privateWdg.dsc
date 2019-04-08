// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";
import * as Branding from "BuildXL.Branding";
import * as DetoursServices from "BuildXL.Sandbox.Windows";
import * as Managed from "Sdk.Managed";

namespace PrivateWdg {
    export declare const qualifier : {
        configuration: "debug" | "release";
        targetFramework: "net472",
        targetRuntime: "win-x64"
    };

    @@public
    export const deployment : Deployment.Definition = {
        contents: [
            importFrom("Private.Wdg.ExecutionLogSdk").dll,
            importFrom("Private.Wdg").deployment,
        ]
    };

    @@public
    export const deployed = BuildXLSdk.Flags.isMicrosoftInternal
        ? BuildXLSdk.DeploymentHelpers.deploy({
                definition: deployment, 
                targetLocation: r`${qualifier.configuration}/private/wdg`, 
            })
        : undefined;
}