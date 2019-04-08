// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace ArbitraryFileGenerator {
    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "ArbitraryFileGenerator",
        rootNamespace: "Tool.ArbitraryFileGenerator",
        skipDocumentationGeneration: true,
        sources: globR(d`.`, "*.cs"),
    });
}
