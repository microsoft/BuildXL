// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace InputListFilter {
    @@public
    export const dll = BuildXLSdk.cacheTest({
        assemblyName: "BuildXL.Cache.InputListFilter.Test",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Cache.VerticalStore").InputListFilter.dll,
            importFrom("BuildXL.Cache.VerticalStore").ImplementationSupport.dll,
            importFrom("BuildXL.Cache.VerticalStore").Interfaces.dll,
            importFrom("BuildXL.Cache.VerticalStore").InMemory.dll,
            importFrom("BuildXL.Cache.VerticalStore").VerticalAggregator.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            Interfaces.dll,
            VerticalAggregator.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
        ],
    });
}
