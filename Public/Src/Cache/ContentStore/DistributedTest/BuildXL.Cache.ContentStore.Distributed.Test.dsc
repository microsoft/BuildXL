// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as XUnit from "Sdk.Managed.Testing.XUnit";
import * as ManagedSdk from "Sdk.Managed";

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
        skipTestRun: BuildXLSdk.restrictTestRunToSomeQualifiers,
        assemblyBindingRedirects: BuildXLSdk.cacheBindingRedirects(),
        appConfig: f`App.config`,
        references: [
            ManagedSdk.Factory.createBinary(importFrom("TransientFaultHandling.Core").Contents.all, r`lib/NET4/Microsoft.Practices.TransientFaultHandling.Core.dll`),
            ...addIf(BuildXLSdk.isFullFramework || qualifier.targetFramework === "netstandard2.0", importFrom("System.Collections.Immutable").pkg),
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
            ...redisPackages,
            ...getSerializationPackages(true),
            Distributed.dll,
            ...Distributed.eventHubPackages,
            UtilitiesCore.dll,
            Hashing.dll,
            Interfaces.dll,
            InterfacesTest.dll,
            Library.dll,
            Test.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Cache.DistributedCache.Host").Service.dll,
            importFrom("BuildXL.Cache.DistributedCache.Host").Configuration.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").KeyValueStore.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            ...getGrpcPackages(true),
            ...importFrom("Sdk.Selfhost.RocksDbSharp").pkgs,

            ...BuildXLSdk.fluentAssertionsWorkaround,

            ...BuildXLSdk.systemThreadingTasksDataflowPackageReference,
            importFrom("WindowsAzure.Storage").pkg,
        ],
        runtimeContent: [
            {
                subfolder: r`redisServer`,
                contents: [
                    ...BuildXLSdk.isTargetRuntimeOsx 
                        ? importFrom("Redis-osx-x64").Contents.all.contents 
                        : importFrom("Redis-64").Contents.all.contents,
                ]
            },
        ],
    });
}
