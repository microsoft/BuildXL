// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";

namespace KeyValueStoreTests {
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.Utilities.KeyValueStore",
        sources: globR(d`.`, "*.cs"),
        nullable: true,
        assemblyBindingRedirects: BuildXLSdk.cacheBindingRedirects(),
        references: [
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").KeyValueStore.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            ...BuildXLSdk.getSystemMemoryPackages(true)
        ],
    });
}
