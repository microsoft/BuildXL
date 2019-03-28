// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace KeyValueStoreTests {
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.Utilities.KeyValueStore",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").KeyValueStore.dll,
            importFrom("BuildXL.Utilities").Native.dll,
        ],
    });
}
