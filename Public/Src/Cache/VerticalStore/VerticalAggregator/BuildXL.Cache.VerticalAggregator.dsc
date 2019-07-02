// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VerticalAggregator {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.VerticalAggregator",
        sources: globR(d`.`, "*.cs"),
        cacheOldNames: [{
            namespace: "VerticalAggregator",
            factoryClass: "VerticalCacheAggregatorFactory",
        }],
        references: [
            ImplementationSupport.dll,
            Interfaces.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
        ],
        internalsVisibleTo: [
            "bxlcacheanalyzer",
            "BuildXL.Cache.VerticalAggregator.Test",
        ],
    });
}
