// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";

namespace StandardSdk.ValueCache {
    export const test = BuildXLSdk.sdkTest({
        testFiles: globR(d`.`, "Test.*.dsc"),
        sdkFolders: [
            d`${Context.getMount("SdkRoot").path}/ValueCache`,
        ],
        // autoFixLkgs: true, // Uncomment this line to have all lkgs automatically updated.
    });
}
