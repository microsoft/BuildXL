// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as XUnit from "Sdk.Managed.Testing.XUnit";
import * as ManagedSdk from "Sdk.Managed";
import { Transformer } from "Sdk.Transformers";

import * as ContentStore from "BuildXL.Cache.ContentStore";

namespace BlobWithLocalCache {
    export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet472;

    @@public
    export const dll = BuildXLSdk.cacheTest({
        assemblyName: "BuildXL.Cache.BlobWithLocalCache.Test",
        sources: globR(d`.`, "*.cs"),
        allowUnsafeBlocks: true,
        runTestArgs: {
                untrackTestDirectory: true, // GRPC server may create memory-mapped files in this directory
                unsafeTestRunArguments: {
                    untrackedPaths: [
                        ...addIfLazy(Context.isWindowsOS() && Environment.getDirectoryValue("CommonProgramFiles") !== undefined,
                            () => [f`${Environment.getDirectoryValue("CommonProgramFiles")}/SSL/openssl.cnf`])
                    ]
                }
            },

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
                importFrom("Azure.Core.Amqp").pkg,
                importFrom("Microsoft.Azure.Amqp").pkg,
                importFrom("System.IdentityModel.Tokens.Jwt").pkg
            ),
            ...ContentStore.getSerializationPackages(true),
            ContentStore.Distributed.dll,
            ContentStore.DistributedTest.dll,
            ...ContentStore.Distributed.eventHubPackages,
            ContentStore.UtilitiesCore.dll,
            ContentStore.Hashing.dll,
            ContentStore.Interfaces.dll,
            Interfaces.dll,
            ContentStore.InterfacesTest.dll,
            ContentStore.Library.dll,
            ContentStore.Test.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Library.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Distributed.dll,
            importFrom("BuildXL.Cache.DistributedCache.Host").Service.dll,
            importFrom("BuildXL.Cache.DistributedCache.Host").Configuration.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").KeyValueStore.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Cache.VerticalStore").Interfaces.dll,
            // Using gRPC.NET hosting implementation from the launcher.
            ...addIfLazy(!BuildXLSdk.isFullFramework, () => [importFrom("BuildXL.Cache.DistributedCache.Host").LauncherServer.exe]),

            ContentStore.Grpc.dll,
            ...importFrom("Sdk.Selfhost.RocksDbSharp").pkgs,

            ...BuildXLSdk.fluentAssertionsWorkaround,

            ...BuildXLSdk.systemThreadingTasksDataflowPackageReference,
            ...ContentStore.getAzureBlobStorageSdkPackages(true),

            ...ContentStore.getGrpcPackages(true),
            ...ContentStore.getProtobufNetPackages(true),

            ...BuildXLSdk.getSystemMemoryPackages(true),
            importFrom("System.ServiceModel.Http").pkg,
            importFrom("System.ServiceModel.Primitives").pkg,

            ...(BuildXLSdk.isFullFramework 
                ? [ 
                    importFrom("System.IO.Pipelines").pkg,
                    importFrom("System.Runtime.CompilerServices.Unsafe").pkg,
                    importFrom("Pipelines.Sockets.Unofficial").pkg,
                ] 
                : [
                    importFrom("System.IO.Pipelines").pkg,            
                    ...(BuildXLSdk.isDotNetCore ? [] : [
                        importFrom("System.Runtime.CompilerServices.Unsafe").pkg,
                    ]),
                    importFrom("Pipelines.Sockets.Unofficial").pkg,
                ]),
            ...BuildXLSdk.systemThreadingChannelsPackages,
            ...BuildXLSdk.bclAsyncPackages,
            // Needed because of snipped dependencies for System.IO.Pipelines and System.Threading.Channels
            importFrom("System.Threading.Tasks.Extensions").pkg,
        ],
        runtimeContent: [
            {
                subfolder: r`azurite`,
                contents: [
                    importFrom("BuildXL.Azurite.Executables").Contents.all
                ]
            },
            importFrom("BuildXL.Cache.VerticalStore").MemoizationStoreAdapter.dll
        ],
    });
}
