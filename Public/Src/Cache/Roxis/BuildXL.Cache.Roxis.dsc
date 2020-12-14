// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";

// Framework support is as follows:
//  - Client application (App dsc) only supports running under .NET Core
//  - Client libraries (Client dsc), gRPC libraries (Grpc dsc), and Client/Server shared code (Common dsc) support .NET 472, and .NET Core
//  - Tests (Test dsc) run under .NET 472 and .NET Core
//  - Server (Server dsc) only supports .NET Core, but maintains some amount of compatibility with .NET 472 so we can run the tests
export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet472AndNetStandard20;

export {BuildXLSdk};

export const NetFx = BuildXLSdk.NetFx;

namespace Default {
    export declare const qualifier : BuildXLSdk.DefaultQualifier;

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
