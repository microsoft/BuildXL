// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import {Transformer} from "Sdk.Transformers";

namespace Interop {
    @@public
    export const dll = BuildXLSdk.library({
        // IMPORTANT!!! Do not add non-bxl dependencies into this project, any non-bxl dependencies should go to BuildXL.Utilities instead
        assemblyName: "BuildXL.Interop",
        sources: [
            ...globR(d`.`, "*.cs"),
            opNamesAutoGen
        ],
        allowUnsafeBlocks: true,
    });
}
