// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";

export declare const qualifier : BuildXLSdk.Net6Qualifier;

export {BuildXLSdk};

namespace Default {
    @@public
    export const deployment: Deployment.Definition = !BuildXLSdk.Flags.isMicrosoftInternal ? undefined :
    {
        contents: [
            {
                subfolder: r`App`,
                contents: [
                    App.exe
                ]
            },
        ]
    };
}
