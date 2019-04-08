// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";

namespace StandardSdk.Testing {
    export const testingTest = BuildXLSdk.sdkTest({
        testFiles: globR(d`.`, "Test.*.dsc"),
        // Lkgs: true, // Uncomment this line to have all lkgs automatically updated.
    });
}
