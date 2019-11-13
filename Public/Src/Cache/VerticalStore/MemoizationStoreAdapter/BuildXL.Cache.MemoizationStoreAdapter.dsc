// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Sdk from "Sdk.Managed";

namespace MemoizationStoreAdapter {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.MemoizationStoreAdapter",
        sources: globR(d`.`, "*.cs"),
        cacheOldNames: [
            {
                namespace: "MemoizationStoreAdapter",
                factoryClass: "CloudStoreLocalCacheServiceFactory",
            },
            {
                namespace: "MemoizationStoreAdapter",
                factoryClass: "MemoizationStoreCacheFactory",
            },
        ],
        references: [
            Interfaces.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.dll,
            importFrom("System.Interactive.Async").pkg,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Library.dll,
            importFrom("BuildXL.Cache.ContentStore").Distributed.dll,
            importFrom("BuildXL.Cache.DistributedCache.Host").Configuration.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Library.dll,
        ],
        internalsVisibleTo: [
            "BuildXL.Cache.MemoizationStoreAdapter.Test",
        ],
  });
}
