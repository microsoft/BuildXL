// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";

namespace StandardSdk.Transformers {
    // skipping this test when running on a non-Windows platform because there are simply too many
    // differences in corresponding LKG files for Windows and Unix platforms.
    export const transformersTest = Context.getCurrentHost().os === "win" && BuildXLSdk.sdkTest({
        testFiles: glob(d`.`, "Test.*.dsc"),
        // autoFixLkgs: true, // Uncomment this line to have all lkgs automatically updated.
    });
}
