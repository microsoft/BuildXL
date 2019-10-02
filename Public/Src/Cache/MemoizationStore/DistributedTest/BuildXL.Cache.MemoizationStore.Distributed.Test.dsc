// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as XUnit from "Sdk.Managed.Testing.XUnit";

namespace DistributedTest {
    @@public
    export const dll = BuildXLSdk.isDotNetCoreBuild ? undefined : BuildXLSdk.cacheTest({
        assemblyName: "BuildXL.Cache.MemoizationStore.Distributed.Test",
        sources: globR(d`.`, "*.cs"),
        runTestArgs: {
                // Need to untrack the test output directory, because redis server tries to write some pdbs.
                untrackTestDirectory: true,
                parallelBucketCount: 8,
            },
        skipTestRun: BuildXLSdk.restrictTestRunToSomeQualifiers,
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Xml.dll,
                NetFx.System.Xml.Linq.dll
            ),
            ContentStore.Hashing.dll,
            ContentStore.UtilitiesCore.dll,
            ContentStore.DistributedTest.dll,
            ContentStore.Distributed.dll,
            ContentStore.InterfacesTest.dll,
            ContentStore.Interfaces.dll,
            ContentStore.Library.dll,
            ContentStore.Test.dll,
            Distributed.dll,
            InterfacesTest.dll,
            Interfaces.dll,
            Library.dll,

            ...BuildXLSdk.fluentAssertionsWorkaround,
            importFrom("StackExchange.Redis.StrongName").pkg,
            importFrom("System.Interactive.Async").pkg,
        ],
        runtimeContent: [
            ...importFrom("Redis-64").Contents.all.contents,
        ],
    });
}
