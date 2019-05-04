// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as MacServices from "BuildXL.Sandbox.MacOS";

namespace MockVmCommandProxy {
    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "VmCommandProxy",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Utilities").dll
        ]
    });
}
