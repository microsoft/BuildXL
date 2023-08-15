// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BlobLifetimeManagerTest {
    @@public
    export const dll = BuildXLSdk.cacheTest({
        assemblyName: "BuildXL.Cache.BlobLifetimeManager.Test",
        sources: globR(d`.`, "*.cs"),
        skipTestRun: BuildXLSdk.restrictTestRunToSomeQualifiers,
        references: [
            // Needed to get Fluent Assertions
            ...BuildXLSdk.fluentAssertionsWorkaround,

            // Needed to access the app's classes
            Library.dll,
            
            importFrom("BuildXL.Cache.ContentStore").InterfacesTest.dll,
            importFrom("BuildXL.Cache.ContentStore").Test.dll,
            importFrom("BuildXL.Cache.ContentStore").DistributedTest.dll,

            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").Library.dll,
            importFrom("BuildXL.Cache.ContentStore").Distributed.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,

            importFrom("BuildXL.Cache.DistributedCache.Host").Configuration.dll,
            
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Library.dll,
            
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("BuildXL.Utilities").KeyValueStore.dll,
            
            ...importFrom("BuildXL.Cache.ContentStore").getAzureBlobStorageSdkPackages(true),
            ...importFrom("Sdk.Selfhost.RocksDbSharp").pkgs,
        ],
        skipDocumentationGeneration: true,
        nullable: true,
        runtimeContent: [
            {
                subfolder: r`azurite`,
                contents: [
                    importFrom("BuildXL.Azurite.Executables").Contents.all
                ]
            }
        ],
    });
}
