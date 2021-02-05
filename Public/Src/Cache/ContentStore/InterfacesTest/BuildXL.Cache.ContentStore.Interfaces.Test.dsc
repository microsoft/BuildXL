// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace InterfacesTest {
    @@public
    export const dll = BuildXLSdk.cacheTest({
        assemblyName: "BuildXL.Cache.ContentStore.Interfaces.Test",
        sources: globR(d`.`,"*.cs"),
        skipTestRun: BuildXLSdk.restrictTestRunToSomeQualifiers,
        references: [
            UtilitiesCore.dll,
            Hashing.dll,
            Interfaces.dll,
            Library.dll,

            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("Newtonsoft.Json").pkg,
            ...getSerializationPackages(/*includeNetStandard*/true),
        ],
        runTestArgs: {
            skipGroups: BuildXLSdk.isDotNetCoreBuild ? [ "SkipDotNetCore" ] : [],
            parallelBucketCount: 16,
        }
    });
}
