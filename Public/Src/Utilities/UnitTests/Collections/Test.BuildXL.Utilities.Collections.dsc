// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Collections {
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.Utilities.Collections",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").dll,
        ],
    });
}
