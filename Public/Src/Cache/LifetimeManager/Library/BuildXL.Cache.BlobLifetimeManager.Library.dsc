// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Library {
    @@public
    export const dll =  BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.BlobLifetimeManager.Library",
        sources: globR(d`.`,"*.cs"),
        references: [
            importFrom("BuildXL.Cache.ContentStore").Distributed.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").Library.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Library.dll,
            
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("BuildXL.Utilities").KeyValueStore.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            
            ...importFrom("BuildXL.Cache.ContentStore").getAzureBlobStorageSdkPackages(true),
            ...importFrom("Sdk.Selfhost.RocksDbSharp").pkgs,
            importFrom("BuildXL.Cache.BuildCacheResource").Helper.dll,
        ],
        skipDocumentationGeneration: true,
        nullable: true,
        internalsVisibleTo: [
            "BuildXL.Cache.BlobLifetimeManager.Test",
        ],
    });
}
