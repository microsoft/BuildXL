// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

namespace PackedExecution {

    export declare const qualifier: BuildXLSdk.NetCoreAppQualifier;

    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.PackedExecution",
        allowUnsafeBlocks: true,
        sources: globR(d`.`, "*.cs"),
        references: [
            Core.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Interop.dll,
            importFrom("BuildXL.Utilities").PackedTable.dll,
            importFrom("BuildXL.Utilities").PackedExecution.dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
        ],
    });
}
