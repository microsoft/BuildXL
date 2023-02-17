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
        allowUnsafeBlocks: true,
        runTestArgs: {
                parallelBucketCount: 8,
                untrackTestDirectory: true, // GRPC server may create memory-mapped files in this directory
                tools: {
                    exec: {
                        environmentVariables: envVars
                    }
                },
            },
        //skipTestRun: BuildXLSdk.restrictTestRunToSomeQualifiers,
        assemblyBindingRedirects: BuildXLSdk.cacheBindingRedirects(),
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
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            Grpc.dll,
            ...getGrpcPackages(true),
            ...importFrom("Sdk.Selfhost.RocksDbSharp").pkgs,

            ...BuildXLSdk.fluentAssertionsWorkaround,

            ...BuildXLSdk.systemThreadingTasksDataflowPackageReference,
            ...getAzureBlobStorageSdkPackages(true),

            ...getGrpcPackages(true),
            
            importFrom("Microsoft.Extensions.Logging.Abstractions.v6.0.0").pkg, // required by grpc.net packages

            ...getProtobufNetPackages(true),
            ...BuildXLSdk.getSystemMemoryPackages(true),
            importFrom("System.ServiceModel.Http").pkg,
            importFrom("System.ServiceModel.Primitives").pkg,

            ...(BuildXLSdk.isFullFramework 
                ? [ 
                    // Needed because net472 -> netstandard2.0 translation is not yet supported by the NuGet resolver.
                    importFrom("System.IO.Pipelines").withQualifier({ targetFramework: "netstandard2.0" }).pkg,
                    importFrom("System.Runtime.CompilerServices.Unsafe").withQualifier({ targetFramework: "netstandard2.0" }).pkg,
                    importFrom("Pipelines.Sockets.Unofficial").withQualifier({ targetFramework: "netstandard2.0" }).pkg,
                ] 
                : [
                    importFrom("System.IO.Pipelines").pkg,            
                    ...(BuildXLSdk.isDotNetCoreApp ? [] : [
                        importFrom("System.Runtime.CompilerServices.Unsafe").pkg,
                    ]),
                    importFrom("Pipelines.Sockets.Unofficial").pkg,
                ]),
            ...BuildXLSdk.systemThreadingChannelsPackages,
            ...BuildXLSdk.bclAsyncPackages,
            // Needed because of snipped dependencies for System.IO.Pipelines and System.Threading.Channels
            importFrom("System.Threading.Tasks.Extensions").pkg,
        ],
        internalsVisibleTo: [
            "BuildXL.Cache.MemoizationStore.Distributed.Test",
        ],
        runtimeContent: [
            // Need to add the dll explicitely to avoid runtime failures for net472
             // required by grpc.net packages
            ...(BuildXLSdk.isFullFramework ? [importFrom("Microsoft.Extensions.Logging.Abstractions.v6.0.0").pkg] : []),
            {
                subfolder: r`azurite`,
                contents: [
                    importFrom("BuildXL.Azurite.Executables").Contents.all
                ]
            }
        ],
    });
}
