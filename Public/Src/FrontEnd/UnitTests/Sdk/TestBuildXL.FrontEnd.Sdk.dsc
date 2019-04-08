// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace FrontEnd.Sdk {
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.FrontEnd.Sdk",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            importFrom("BuildXL.Utilities").dll,
        ]
    });
}
