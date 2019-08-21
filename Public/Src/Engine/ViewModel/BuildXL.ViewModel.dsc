// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

namespace ViewModel {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.ViewModel",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
        ],
    });
}
