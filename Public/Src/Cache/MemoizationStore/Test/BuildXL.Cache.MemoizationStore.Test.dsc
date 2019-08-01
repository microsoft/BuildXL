// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Test {
    @@public
    export const dll = BuildXLSdk.cacheTest({
        assemblyName: "BuildXL.Cache.MemoizationStore.Test",
        sources: globR(d`.`,"*.cs"),
        skipTestRun: BuildXLSdk.restrictTestRunToSomeQualifiers,
        runTestArgs: {
            skipGroups: ["Performance"], // Don't run the performance tests in our normal validation flow
            parallelBucketCount: 12,
        },
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Data.dll,
                NetFx.System.Xml.dll,
                NetFx.System.Xml.Linq.dll
            ),
            ContentStore.Distributed.dll,
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
            importFrom("System.Data.SQLite.Core").pkg,
            importFrom("System.Interactive.Async").pkg,
            ...BuildXLSdk.fluentAssertionsWorkaround,
        ],
    });
}
