// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";

namespace StandardSdk.Transformers {
    export const transformersTest = BuildXLSdk.sdkTest({
        testFiles: glob(d`.`, "Test.*.dsc"),
        // autoFixLkgs: true, // Uncomment this line to have all lkgs automatically updated.
    });
}
