// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VSCode.DebugAdapter {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "VSCode.DebugAdapter",
        sources: globR(d`.`, "*.cs"),
        references: [
            DebugProtocol.dll,
            importFrom("Newtonsoft.Json").pkg,
        ],
    });
}
