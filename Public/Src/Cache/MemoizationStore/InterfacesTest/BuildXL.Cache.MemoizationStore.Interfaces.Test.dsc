// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace InterfacesTest {
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "BuildXL.Cache.MemoizationStore.Interfaces.Test",
        sources: globR(d`.`,"*.cs"),
        skipTestRun: BuildXLSdk.restrictTestRunToSomeQualifiers,
        references: [
            ContentStore.UtilitiesCore.dll,
            ContentStore.Hashing.dll,
            ContentStore.Interfaces.dll,
            ContentStore.InterfacesTest.dll,
            ContentStore.Library.dll,
            Interfaces.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("System.Interactive.Async").pkg,
        ],
    });
}
