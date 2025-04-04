
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed  from "Sdk.Managed";

namespace PackedTable {

    export declare const qualifier: BuildXLSdk.Net6PlusQualifier;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.PackedTable",
        generateLogs: false,
        sources: [
            ...globR(d`.`, "*.cs"),
        ],
        references: [
            $.dll,
            Native.dll,
            Utilities.Core.dll,
        ],
        internalsVisibleTo: [
            "Test.BuildXL.PackedExecution",
        ],
    });
}
