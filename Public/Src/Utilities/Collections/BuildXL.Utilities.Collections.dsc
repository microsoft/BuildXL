// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
namespace Collections {

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Utilities.Collections",
        allowUnsafeBlocks: true,
        sources: globR(d`.`, "*.cs"),
        nullable: true,
        internalsVisibleTo: [
            "Test.BuildXL.Utilities.Collections",
        ],
        references: [
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll
				]
    });
}
