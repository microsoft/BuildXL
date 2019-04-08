// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace ImplementationSupport {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.ImplementationSupport",
        allowUnsafeBlocks: true,
        sources: globR(d`.`, "*.cs"),
        references: [
            Interfaces.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("Newtonsoft.Json").pkg,
        ],
    });
}
