// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace InterfacesTest {
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "BuildXL.Cache.ContentStore.Interfaces.Test",
        sources: globR(d`.`,"*.cs"),
        skipTestRun: BuildXLSdk.restrictTestRunToDebugNet461OnWindows,
        references: [
            UtilitiesCore.dll,
            Hashing.dll,
            Interfaces.dll,
            Library.dll,

            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
        ],
        runTestArgs: {
            skipGroups: BuildXLSdk.isDotNetCoreBuild ? [ "SkipDotNetCore" ] : []
        }
    });
}
