// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Test {
    export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet472;
    
    @@public
    export const dll = BuildXLSdk.cacheTest({
        assemblyName: "BuildXL.Cache.MemoizationStore.Test",
        sources: globR(d`.`,"*.cs"),
        skipTestRun: BuildXLSdk.restrictTestRunToSomeQualifiers,
        runTestArgs: {
            skipGroups: ["Performance"], // Don't run the performance tests in our normal validation flow
            parallelBucketCount: 12,
        },
        assemblyBindingRedirects: BuildXLSdk.cacheBindingRedirects(),
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Data.dll,
                NetFx.System.Xml.dll,
                NetFx.System.Xml.Linq.dll
            ),
            ContentStore.Distributed.dll,
            ContentStore.DistributedTest.dll,
            ContentStore.Hashing.dll,
            ContentStore.UtilitiesCore.dll,
            ContentStore.Interfaces.dll,
            ContentStore.InterfacesTest.dll,
            ContentStore.Library.dll,
            ContentStore.Test.dll,
            ContentStore.Grpc.dll,
            Interfaces.dll,
            InterfacesTest.dll,
            Library.dll,

            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            ...importFrom("BuildXL.Cache.ContentStore").redisPackages,
            ...BuildXLSdk.bclAsyncPackages,
            ...BuildXLSdk.fluentAssertionsWorkaround,
            ...BuildXLSdk.systemThreadingTasksDataflowPackageReference,
        ],
    });
}
