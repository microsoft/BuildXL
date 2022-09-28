// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

namespace BuildCacheAdapter {
    @@public
    export const dll = !BuildXLSdk.Flags.isVstsArtifactsEnabled ? undefined : BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.BuildCacheAdapter",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Authentication.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
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

            ...BuildXLSdk.visualStudioServicesArtifactServicesWorkaround,
            ...importFrom("BuildXL.Cache.ContentStore").getAzureBlobStorageSdkPackages(true),
        ]
    });
}
