// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import {Transformer} from "Sdk.Transformers";

namespace Interop {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Interop",
        sources: [
            ...globR(d`.`, "*.cs"),
            opNamesAutoGen
        ],
        allowUnsafeBlocks: true,
    });
}
