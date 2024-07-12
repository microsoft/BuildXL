// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";

namespace Test {
    @@public
    export const dll = BuildXLSdk.cacheTest({
        assemblyName: "BuildXL.Cache.BuildCacheResource.Helper.Test",
        sources: globR(d`.`, "*.cs"),
        references: [
            Helper.dll,
        ],
        // We use a mock version of an expected cache resource configuration file
        runtimeContent: [ f`BuildCacheConfig.json`]
    });
}
