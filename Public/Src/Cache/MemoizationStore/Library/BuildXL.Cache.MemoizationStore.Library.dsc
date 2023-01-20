// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Library {
    export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet472AndNetStandard20;
    
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.MemoizationStore",
        sources: globR(d`.`,"*.cs"),
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Data.dll,
                NetFx.System.Runtime.Serialization.dll
            ),
            ContentStore.Distributed.dll,
            ContentStore.UtilitiesCore.dll,
            ContentStore.Grpc.dll,
            ContentStore.Hashing.dll,
            ContentStore.Interfaces.dll,
            ContentStore.Library.dll,
            Interfaces.dll,
            importFrom("BuildXL.Cache.DistributedCache.Host").Configuration.dll,
            
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,

            ...BuildXLSdk.bclAsyncPackages,
            
            ...importFrom("BuildXL.Cache.ContentStore").getGrpcPackages(true),
        ],
        allowUnsafeBlocks: true,
        internalsVisibleTo: [
            "BuildXL.Cache.MemoizationStore.Test",
            "BuildXL.Cache.MemoizationStore.Distributed.Test",
        ]
    });
}
