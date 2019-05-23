// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Vsts {
    @@public
    export const dll = !BuildXLSdk.Flags.isVstsArtifactsEnabled ? undefined : BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.MemoizationStore.Vsts",
        sources: globR(d`.`,"*.cs"),
        references: [
            ...addIfLazy(BuildXLSdk.isFullFramework, () => [
                importFrom("Microsoft.AspNet.WebApi.Client").pkg,
                NetFx.System.Net.Http.dll,
                NetFx.System.Runtime.Serialization.dll,
            ]),
            Library.dll,
            Interfaces.dll,
            VstsInterfaces.dll,
            ContentStore.UtilitiesCore.dll,
            ContentStore.Hashing.dll,
            ContentStore.Interfaces.dll,
            ContentStore.Library.dll,
            ContentStore.Vsts.dll,
            importFrom("Newtonsoft.Json.v10").pkg,
            importFrom("System.Interactive.Async").pkg,
            importFrom("Microsoft.VisualStudio.Services.Client").pkg,
            BuildXLSdk.Factory.createBinary(importFrom("TransientFaultHandling.Core").Contents.all, r`/lib/NET4/Microsoft.Practices.TransientFaultHandling.Core.dll`),
            ...BuildXLSdk.visualStudioServicesArtifactServicesSharedPkg,
        ],
        allowUnsafeBlocks: true,
        internalsVisibleTo: [
            "BuildXL.Cache.MemoizationStore.Vsts.Test",
        ],
    });
}
