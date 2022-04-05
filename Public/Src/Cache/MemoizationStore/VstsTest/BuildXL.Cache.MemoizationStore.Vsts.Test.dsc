// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VstsTest {
    export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet472;
    
    @@public
    export const dll = !BuildXLSdk.Flags.isVstsArtifactsEnabled || BuildXLSdk.isDotNetCoreBuild ? undefined : BuildXLSdk.cacheTest({
        assemblyName: "BuildXL.Cache.MemoizationStore.Vsts.Test",
        sources: globR(d`.`,"*.cs"),
        appConfig: f`App.config`,
        skipTestRun: BuildXLSdk.restrictTestRunToSomeQualifiers,
        runTestArgs: {
            unsafeTestRunArguments: {
                untrackedPaths: [
                    f`D:\a\1\s\msvs\x64\RELEASE_DEVELOPER\memurai-services.pdb`,
                    f`D:\a\1\s\msvs\x64\RELEASE_DEVELOPER\redis-server.pdb`,
                ],
            },
        },
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

            importFrom("Newtonsoft.Json").pkg,
            ...importFrom("BuildXL.Cache.ContentStore").redisPackages,
            importFrom("Microsoft.VisualStudio.Services.Client").pkg,
            ...BuildXLSdk.visualStudioServicesArtifactServicesWorkaround,
            ...BuildXLSdk.fluentAssertionsWorkaround,
            
            importFrom("BuildXL.Utilities").Authentication.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
        ],
        runtimeContent: [
            {
                subfolder: r`redisServer`,
                contents: [
                    ...BuildXLSdk.isTargetRuntimeOsx 
                        ? importFrom("Redis-osx-x64").Contents.all.contents 
                        : importFrom("MemuraiDeveloper").Contents.all.contents,
                ]
            },
            ...addIf(BuildXLSdk.isFullFramework,
                importFrom("Microsoft.VisualStudio.Services.BlobStore.Client").pkg
            ),
        ],
    });
}
