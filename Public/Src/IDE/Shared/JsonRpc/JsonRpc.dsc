// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";

namespace IDE.Shared.JsonRpc {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "IDE.Shared.JsonRpc",
        sources: globR(d`.`, "*.cs"),
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Runtime.Serialization.dll
            ),
        ],
    });
};
