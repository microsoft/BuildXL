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
        assemblyBindingRedirects: [
            // System.Memory 4.5.4 is a bit weird, because net461 version references System.Buffer.dll v.4.0.3.0
            // but System.Memory.dll from netstandard2.0 references Ssstem.Buffer.dll v.4.0.2.0!
            // And the rest of the world references System.Buffer.dll v.4.0.3.0
            // So we need to have a redirect to solve this problem.
            {
                name: "System.Buffers",
                publicKeyToken: "cc7b13ffcd2ddd51",
                culture: "neutral",
                oldVersion: "0.0.0.0-5.0.0.0",
                newVersion: "4.0.3.0",
            },
            // Different packages reference different version of this assembly.
            {
                name: "System.Runtime.CompilerServices.Unsafe",
                publicKeyToken: "b03f5f7f11d50a3a",
                culture: "neutral",
                oldVersion: "0.0.0.0-5.0.0.0",
                newVersion: "4.0.6.0",
            },
            {
                name: "System.Numerics.Vectors",
                publicKeyToken: "b03f5f7f11d50a3a",
                culture: "neutral",
                oldVersion: "0.0.0.0-4.1.4.0",
                newVersion: "4.1.4.0",
            },
        ],
        appConfig: f`App.config`,
        references: [
            
            ManagedSdk.Factory.createBinary(importFrom("TransientFaultHandling.Core").Contents.all, r`lib/NET4/Microsoft.Practices.TransientFaultHandling.Core.dll`),

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
            ...Distributed.eventHubPackagages,
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
            importFrom("Grpc.Core").pkg,
            importFrom("Grpc.Core.Api").pkg,
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
