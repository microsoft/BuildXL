// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VerticalAggregator {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "BuildXL.Cache.VerticalAggregator.Test",
        sources: globR(d`.`, "*.cs"),
        references: [
            Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Cache.VerticalStore").Interfaces.dll,
            importFrom("BuildXL.Cache.VerticalStore").VerticalAggregator.dll,
            importFrom("BuildXL.Cache.VerticalStore").InMemory.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Storage.dll,
        ],
    });
}
