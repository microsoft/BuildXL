// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Vsts {
    export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet472AndNetStandard20;
    
    @@public
    export const dll = !BuildXLSdk.Flags.isVstsArtifactsEnabled ? undefined : BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.MemoizationStore.Vsts",
        sources: globR(d`.`,"*.cs"),
        references: [
            ...addIfLazy(BuildXLSdk.isFullFramework, () => [
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
            importFrom("Newtonsoft.Json").pkg,
            ...BuildXLSdk.bclAsyncPackages,
            importFrom("Microsoft.VisualStudio.Services.Client").pkg,
            importFrom("Microsoft.AspNet.WebApi.Client").pkg,
            ...BuildXLSdk.visualStudioServicesArtifactServicesWorkaround,
            ...BuildXLSdk.systemThreadingTasksDataflowPackageReference,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
        ],
        allowUnsafeBlocks: true,
        internalsVisibleTo: [
            "BuildXL.Cache.MemoizationStore.Vsts.Test",
        ],
    });
}
