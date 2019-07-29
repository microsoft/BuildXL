// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace DistributedTest {
    @@public
    export const dll = BuildXLSdk.isDotNetCoreBuild ? undefined : BuildXLSdk.test({
        assemblyName: "BuildXL.Cache.MemoizationStore.Distributed.Test",
        sources: globR(d`.`,"*.cs"),
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
        ],
    });
}
