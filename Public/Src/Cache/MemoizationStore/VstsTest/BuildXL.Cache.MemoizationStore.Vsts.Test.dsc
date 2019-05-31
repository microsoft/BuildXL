// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VstsTest {
    @@public
    export const dll = !BuildXLSdk.Flags.isVstsArtifactsEnabled || BuildXLSdk.isDotNetCoreBuild ? undefined : BuildXLSdk.cacheTest({
        assemblyName: "BuildXL.Cache.MemoizationStore.Vsts.Test",
        sources: globR(d`.`,"*.cs"),
        appConfig: f`App.Config`,
        skipTestRun: BuildXLSdk.restrictTestRunToDebugNet461OnWindows,
        references: [
            ...addIfLazy(BuildXLSdk.isFullFramework, () => [
                NetFx.System.Xml.dll,
                NetFx.System.Xml.Linq.dll,
            ]),
            ContentStore.Distributed.dll,
            ContentStore.DistributedTest.dll,
            ContentStore.UtilitiesCore.dll,
            ContentStore.Hashing.dll,
            ContentStore.Interfaces.dll,
            ContentStore.InterfacesTest.dll,
            ContentStore.Library.dll,
            ContentStore.Test.dll,
            ContentStore.Vsts.dll,
            Distributed.dll,
            InterfacesTest.dll,
            Interfaces.dll,
            Library.dll,
            VstsInterfaces.dll,
            Vsts.dll,

            importFrom("Newtonsoft.Json.v10").pkg,
            importFrom("StackExchange.Redis.StrongName").pkg,
            importFrom("Microsoft.VisualStudio.Services.Client").pkg,
            ...BuildXLSdk.visualStudioServicesArtifactServicesSharedPkg,
            ...BuildXLSdk.fluentAssertionsWorkaround,
        ],
        deploymentOptions: {
            excludedDeployableItems: [
            // This code uses newtonsoft v10 but depends transitively on code with the latest version. This needs to use v10, so block deployment of the latest version.
            importFrom("Newtonsoft.Json").pkg,
        ]},
        runtimeContent: [
            ...importFrom("Redis-64").Contents.all.contents,
            ...addIf(BuildXLSdk.isFullFramework,
                importFrom("Microsoft.VisualStudio.Services.BlobStore.Client").pkg
            ),
        ],
    });
}
