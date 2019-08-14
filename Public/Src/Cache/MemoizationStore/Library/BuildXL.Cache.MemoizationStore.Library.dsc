// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Library {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.MemoizationStore",
        sources: globR(d`.`,"*.cs"),
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Data.dll
            ),
            ContentStore.Distributed.dll,
            ContentStore.UtilitiesCore.dll,
            ContentStore.Grpc.dll,
            ContentStore.Hashing.dll,
            ContentStore.Interfaces.dll,
            ContentStore.Library.dll,
            Interfaces.dll,
            
            importFrom("BuildXL.Cache.DistributedCache.Host").Configuration.dll,
            importFrom("BuildXL.Cache.DistributedCache.Host").Service.dll,
            importFrom("BuildXL.Utilities").dll,

            importFrom("System.Data.SQLite.Core").pkg,
            importFrom("System.Interactive.Async").pkg,

            importFrom("Grpc.Core").pkg,
            importFrom("Google.Protobuf").pkg,
            BuildXLSdk.Factory.createBinary(importFrom("TransientFaultHandling.Core").pkg.contents, r`lib/NET4/Microsoft.Practices.TransientFaultHandling.Core.dll`),
        ],
        allowUnsafeBlocks: true,
        runtimeContent: [
            importFrom("Sdk.SelfHost.Sqlite").runtimeLibs,
        ],
        internalsVisibleTo: [
            "BuildXL.Cache.MemoizationStore.Test"
        ]
    });
}
