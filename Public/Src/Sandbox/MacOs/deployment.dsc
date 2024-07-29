// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as SdkDeployment from "Sdk.Deployment";

namespace Deployment {
    export declare const qualifier: {configuration: "debug" | "release"};

    const EnvScript = f`${Context.getMount("Sandbox").path}/MacOs/scripts/env.sh`;

    @@public
    export const buildXLScripts: SdkDeployment.Definition = {
        contents: [
            f`${Context.getMount("Sandbox").path}/MacOs/scripts/bxl.sh`,
            f`${Context.getMount("Sandbox").path}/MacOs/scripts/bxl.sh.1`,
            EnvScript
        ]
    };
}
