// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

namespace BuildCacheAdapter {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    @@public
    export const dll = !BuildXLSdk.Flags.isVstsArtifactsEnabled ? undefined : BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.BuildCacheAdapter",
        sources: globR(d`.`, "*.cs"),
        cacheOldNames: [{
            namespace: "BuildCacheAdapter",
            factoryClass: "BuildCacheFactory",
        },{
            namespace: "BuildCacheAdapter",
            factoryClass: "DistributedBuildCacheFactory",
        }],
        references: [
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            Interfaces.dll,
            MemoizationStoreAdapter.dll,
            importFrom("Microsoft.VisualStudio.Services.InteractiveClient").pkg,
            importFrom("Microsoft.VisualStudio.Services.Client").pkg,
            importFrom("Microsoft.IdentityModel.Clients.ActiveDirectory").pkg,
            importFrom("Microsoft.VisualStudio.Services.BlobStore.Client").pkg,

            ...addIf(BuildXLSdk.isDotNetCoreBuild,
                Managed.Factory.createBinary(importFrom("Microsoft.Net.Http").Contents.all, r`lib/portable-net40+sl4+win8+wp71+wpa81/System.Net.Http.Primitives.dll`)
            ),

			importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Library.dll,
            importFrom("BuildXL.Cache.ContentStore").Vsts.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Distributed.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Library.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Vsts.dll,
            importFrom("Microsoft.AspNet.WebApi.Client").pkg,

            ...BuildXLSdk.visualStudioServicesArtifactServicesSharedPkg,
            importFrom("StackExchange.Redis.StrongName").pkg,
            importFrom("WindowsAzure.Storage").pkg,
        ],
        runtimeContentToSkip: [
            importFrom("Newtonsoft.Json.v10").pkg, // CloudStore has to reply on NewtonSoft.Json version 10. BuildXL and asp.net core depend on 11.
            importFrom("Newtonsoft.Json.v10").withQualifier({targetFramework: "net451"}).pkg, // CloudStore hardcodes net451 in certain builds so exclude that one too.
        ]
    });
}
