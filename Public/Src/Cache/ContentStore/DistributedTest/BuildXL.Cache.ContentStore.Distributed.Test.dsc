// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as XUnit from "Sdk.Managed.Testing.XUnit";
import * as ManagedSdk from "Sdk.Managed";
import { Transformer } from "Sdk.Transformers";

namespace DistributedTest {
    export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet472;

    const storageKeyEnvVar = "TestEventHub_StorageAccountKey";
    const storageNameEnvVar = "TestEventHub_StorageAccountName";
    const ehConStrEnvVar = "TestEventHub_EventHubConnectionString";
    const ehNameEnvVar = "TestEventHub_EventHubName";
    const ehUriEnvVar = "TestEventHub_FullyQualifiedUriEnvVar";
    const ehGroupNameEnvVar = "TestEventHub_EventHubManagedIdentityId";
    const ehPathEnvVar = "TestEventHub_EventHubPath";
    const ehConsumerEnvVar = "TestEventHub_EventHubConsumerGroupName";

    const envVars = [ 
        sToEnvVar(storageKeyEnvVar),
        sToEnvVar(storageNameEnvVar),
        sToEnvVar(ehConStrEnvVar),
        sToEnvVar(ehNameEnvVar),
        sToEnvVar(ehUriEnvVar),
        sToEnvVar(ehGroupNameEnvVar),
        sToEnvVar(ehPathEnvVar),
        sToEnvVar(ehConsumerEnvVar),
    ];

    export function sToEnvVar(s: string) : Transformer.EnvironmentVariable {
        return { name: s, value: Environment.hasVariable(s) ? Environment.getStringValue(s) : "" };
    }

    @@public
    export const dll = BuildXLSdk.cacheTest({
        assemblyName: "BuildXL.Cache.ContentStore.Distributed.Test",
        sources: globR(d`.`, "*.cs"),
        runTestArgs: {
                // Need to untrack the test output directory, because redis server tries to write some pdbs.
                untrackTestDirectory: true,
                parallelBucketCount: 8,
                tools: {
                    exec: {
                        environmentVariables: envVars
                    }
                }
            },
        skipTestRun: BuildXLSdk.restrictTestRunToSomeQualifiers,
        assemblyBindingRedirects: BuildXLSdk.cacheBindingRedirects(),
        appConfig: f`App.config`,
        references: [
            ...addIf(BuildXLSdk.isFullFramework, importFrom("System.Collections.Immutable").pkg),
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
            Grpc.dll,
            ...getGrpcPackages(true),
            ...importFrom("Sdk.Selfhost.RocksDbSharp").pkgs,

            ...BuildXLSdk.fluentAssertionsWorkaround,

            ...BuildXLSdk.systemThreadingTasksDataflowPackageReference,
            importFrom("WindowsAzure.Storage").pkg,

            ...getGrpcPackages(true),
            ...getProtobufNetPackages(true),
            ...BuildXLSdk.getSystemMemoryPackages(true),
            importFrom("System.ServiceModel.Http").pkg,
            importFrom("System.ServiceModel.Primitives").pkg,
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
