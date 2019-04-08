// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VSCode.DebugProtocol {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "VSCode.DebugProtocol",
        sources: globR(d`.`, "*.cs"),
    });
}
