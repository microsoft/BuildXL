// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as XUnit from "Sdk.Managed.Testing.XUnit";

namespace DistributedTest {
    @@public
    export const dll = BuildXLSdk.cacheTest({
            assemblyName: "BuildXL.Cache.ContentStore.Distributed.Test",
            sources: globR(d`.`, "*.cs"),
            runTestArgs: {
                    // Need to untrack the test output directory, because redis server tries to write some pdbs.
                    untrackTestDirectory: true,
                    parallelBucketCount: 8,
                },
            skipTestRun: BuildXLSdk.restrictTestRunToDebugNet461OnWindows,
            references: [
                ...addIf(BuildXLSdk.isFullFramework,
                    NetFx.System.IO.dll,
                    NetFx.System.Net.Primitives.dll,
                    NetFx.System.Xml.dll,
                    NetFx.System.Xml.Linq.dll
                ),
                ...addIf(BuildXLSdk.isFullFramework,
                    importFrom("Microsoft.Azure.Amqp").pkg,
                    importFrom("Microsoft.Azure.Services.AppAuthentication").pkg,
                    importFrom("Microsoft.IdentityModel.Clients.ActiveDirectory").pkg,
                    importFrom("System.IdentityModel.Tokens.Jwt").pkg
                ),
                Distributed.dll,
                ...Distributed.eventHubPackagages,
                UtilitiesCore.dll,
                Hashing.dll,
                Interfaces.dll,
                InterfacesTest.dll,
                Library.dll,
                Test.dll,
                importFrom("BuildXL.Utilities").dll,
                importFrom("BuildXL.Utilities").Collections.dll,
                importFrom("BuildXL.Utilities").KeyValueStore.dll,
                importFrom("BuildXL.Utilities").Native.dll,
                importFrom("Grpc.Core").pkg,
                importFrom("Sdk.Selfhost.RocksDbSharp").pkg,

                importFrom("StackExchange.Redis.StrongName").pkg,
                importFrom("xunit.abstractions").withQualifier({targetFramework: "netstandard2.0"}).pkg,
                ...BuildXLSdk.fluentAssertionsWorkaround,

                importFrom("WindowsAzure.Storage").pkg,
            ],
            runtimeContent: [
                ...importFrom("Redis-64").Contents.all.contents,
            ],
        });
}
