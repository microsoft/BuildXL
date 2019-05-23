// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Test {
    @@public
    export const dll = BuildXLSdk.cacheTest({
        assemblyName: "BuildXL.Cache.MemoizationStore.Test",
        sources: globR(d`.`,"*.cs"),
        skipTestRun: BuildXLSdk.restrictTestRunToDebugNet461OnWindows,
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
            ...(BuildXLSdk.isDotNetCoreBuild
                // TODO: This is to get a .Net Core build, but it may not pass tests
                ? [importFrom("System.Data.SQLite.Core").withQualifier({targetFramework: "net461"}).pkg]
                // Gets around issue of 461 needed netstandard 2.0 lib
                : [importFrom("System.Data.SQLite.Core").pkg]
            ),
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
            importFrom("System.Interactive.Async").pkg,
            ...BuildXLSdk.fluentAssertionsWorkaround,
        ],
    });
}
