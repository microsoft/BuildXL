// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
namespace ToolSupport {

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.ToolSupport",
        sources: globR(d`.`, "*.cs"),
        references: [
            $.dll,
            Collections.dll,
            Utilities.Core.dll
        ],
    });
}
