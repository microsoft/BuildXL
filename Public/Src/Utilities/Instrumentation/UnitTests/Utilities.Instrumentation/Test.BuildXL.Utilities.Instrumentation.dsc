// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

namespace Core {
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.Utilities.Instrumentation",
        allowUnsafeBlocks: true,
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
        ],
    });
}
