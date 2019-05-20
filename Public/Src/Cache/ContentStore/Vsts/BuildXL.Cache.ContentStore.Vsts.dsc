// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Vsts {
    @@public
    export const dll = !BuildXLSdk.Flags.isVstsArtifactsEnabled ? undefined : BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.ContentStore.Vsts",
        sources: globR(d`.`,"*.cs"),
        references: [
            ...addIfLazy(BuildXLSdk.isFullFramework, () => [
                NetFx.System.Net.Http.dll,
            ]),
                importFrom("Microsoft.VisualStudio.Services.ArtifactServices.Shared").pkg,
                importFrom("Microsoft.VisualStudio.Services.BlobStore.Client").pkg,
            importFrom("Microsoft.VisualStudio.Services.Client").pkg,
            importFrom("Microsoft.VisualStudio.Services.InteractiveClient").pkg,
            importFrom("WindowsAzure.Storage").pkg,
            BuildXLSdk.Factory.createBinary(importFrom("TransientFaultHandling.Core").Contents.all, r`lib/NET4/Microsoft.Practices.TransientFaultHandling.Core.dll`),
            UtilitiesCore.dll,
            Hashing.dll,
            Interfaces.dll,
            Library.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
        ],
        internalsVisibleTo: [
            "BuildXL.Cache.MemoizationStore.Vsts",
            "BuildXL.Cache.MemoizationStore.Vsts.Test",
        ],
    });
}
