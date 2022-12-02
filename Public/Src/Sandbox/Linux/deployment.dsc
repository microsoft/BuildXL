// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as SdkDeployment from "Sdk.Deployment";

namespace Deployment {
    export declare const qualifier: {configuration: "debug" | "release", targetRuntime: "linux-x64"};

    @@public
    export const natives : SdkDeployment.Definition = Context.getCurrentHost().os === "unix" && {
        contents: [
            Sandbox.libBxlUtils,
            Sandbox.bxlEnv,
            Sandbox.libBxlAudit,
            Sandbox.libDetours
        ]
    };
}