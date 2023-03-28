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
            // IMPORTANT!!! Do not add non-bxl dependencies into this project, any non-bxl dependencies should go to BuildXL.Utilities instead

            ...addIfLazy(!BuildXLSdk.isDotNetCore, () => [
                NetFx.Netstandard.dll,
                importFrom("System.Memory").withQualifier({targetFramework: "netstandard2.0"}).pkg,
            ]),
        ]
    });
}
