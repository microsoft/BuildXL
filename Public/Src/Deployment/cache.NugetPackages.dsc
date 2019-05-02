// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as Nuget from "Sdk.Managed.Tools.NuGet";

namespace Cache.NugetPackages {
    export declare const qualifier : { configuration: "debug" | "release"};

    export const tools : Deployment.Definition = {
        contents: [
            {
                subfolder: r`tools`,
                contents: [

                ]
            },
        ]
    };

    export const libraries : Deployment.Definition = {
        contents: [

        ]
    };

    export const interfaces : Deployment.Definition = {
        contents: [

        ]
    };

    export const hashing : Deployment.Definition = {
        contents: [

        ]
    };
}