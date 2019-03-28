// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Mimic {
    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "Mimic",
        rootNamespace: "Tool.Mimic",
        skipDocumentationGeneration: true,
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Utilities").ToolSupport.dll,
        ],
    });
}
