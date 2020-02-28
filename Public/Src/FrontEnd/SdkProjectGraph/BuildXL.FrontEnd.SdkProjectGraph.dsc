// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace SdkProjectGraph {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.FrontEnd.SdkProjectGraph",
        generateLogs: false,
        sources: globR(d`.`, "*.cs"),
        references: [
            ...BuildXLSdk.tplPackages,
            importFrom("BuildXL.Utilities").dll,
        ],
    });
}
