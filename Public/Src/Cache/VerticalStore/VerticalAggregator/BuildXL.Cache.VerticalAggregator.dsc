// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VerticalAggregator {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.VerticalAggregator",
        sources: globR(d`.`, "*.cs"),
        references: [
            ImplementationSupport.dll,
            Interfaces.dll,
            InMemory.dll,
            importFrom("BuildXL.Engine").Cache.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
        ],
        internalsVisibleTo: [
            "bxlcacheanalyzer",
            "BuildXL.Cache.VerticalAggregator.Test",
        ],
    });
}
