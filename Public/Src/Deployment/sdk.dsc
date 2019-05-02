// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as BuildXLSdk from "Sdk.BuildXL";

namespace Sdks {
     export declare const qualifier : {
        configuration: "debug" | "release",
        targetRuntime: "osx-x64" | "win-x64";
    };

    // Note: Some Sdk's ship with BuildXL. See: /Public/Src/App/Deployment.InBoxSdks.dsc
    const sdkRoot = d`${Context.getMount("sdkRoot").path}`;

    /** We copy the sdk's for now. In the future the sdks can contain compiled helpers */
    @@public
    export const deployment : Deployment.Definition = Deployment.createFromDisk(sdkRoot);

    @@public
    export const deployed = BuildXLSdk.DeploymentHelpers.deploy({
        definition: deployment,
        targetLocation: r`${qualifier.configuration}/sdk`,
    });
}