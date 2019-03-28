// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
namespace Collections {

    // Utilities is used by CloudStore and this is used by Utilities, so it must remain net451 compatible
    export declare const qualifier: BuildXLSdk.DefaultQualifierWithNet451;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Utilities.Collections",
        allowUnsafeBlocks: true,
        sources: globR(d`.`, "*.cs"),
        internalsVisibleTo: [
            "Test.BuildXL.Utilities.Collections",
        ],
    });
}
