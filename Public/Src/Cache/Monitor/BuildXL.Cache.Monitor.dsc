// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";

export declare const qualifier: BuildXLSdk.DefaultQualifier;

export {BuildXLSdk};

namespace Default {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    @@public
    export const deployment: Deployment.Definition =
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
