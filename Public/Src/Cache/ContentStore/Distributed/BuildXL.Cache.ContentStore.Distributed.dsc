// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
import * as ManagedSdk from "Sdk.Managed";

namespace Distributed {
    export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet472AndNetStandard20;
    
    @@public
    export const eventHubPackages = [
        importFrom("Microsoft.Azure.EventHubs").pkg,
        importFrom("Azure.Identity").pkg,
        importFrom("Azure.Core").pkg,
        importFrom("Microsoft.Azure.Services.AppAuthentication").pkg,
        // Microsoft.Azure.EventHubs removes 'System.Diagnostics.DiagnosticSource' as the dependency to avoid deployment issue for .netstandard2.0, but this dependency
        // is required for non-.net core builds.
        ...(BuildXLSdk.isFullFramework 
            ? [ importFrom("System.Diagnostics.DiagnosticSource").pkg, 
                importFrom("Microsoft.IdentityModel.Tokens").pkg,
                importFrom("Microsoft.IdentityModel.Logging").pkg 
              ] 
            : []),
        importFrom("Microsoft.Azure.Amqp").pkg,
    ];

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.ContentStore.Distributed",
        sources: globR(d`.`,"*.cs"),
        embeddedResources: [
            {
                linkedContent: glob(d`Redis/Scripts`,"*.lua"),
            },
        ],
        allowUnsafeBlocks: true,
        references: [
            ...eventHubPackages,
            // Intentionally using different Azure storage package
            importFrom("WindowsAzure.Storage").pkg,
            ...addIf(BuildXLSdk.isFullFramework, BuildXLSdk.withQualifier({targetFramework: "net472"}).NetFx.Netstandard.dll),
            ...addIf(BuildXLSdk.isFullFramework || qualifier.targetFramework === "netstandard2.0", importFrom("System.Collections.Immutable").pkg),
            ...BuildXLSdk.systemThreadingTasksDataflowPackageReference,
            ...redisPackages,

            UtilitiesCore.dll,
            Hashing.dll,
            Interfaces.dll,
            Library.dll,
            importFrom("BuildXL.Cache.DistributedCache.Host").Configuration.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.IO.dll,
                NetFx.System.IO.Compression.dll,
                NetFx.System.IO.Compression.FileSystem.dll,
                NetFx.System.Net.Http.dll,
                NetFx.System.Web.dll
            ),
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Branding.dll,
            importFrom("BuildXL.Utilities").KeyValueStore.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            ...importFrom("Sdk.Selfhost.RocksDbSharp").pkgs,
            Grpc.dll,

            ...getGrpcPackages(true),
            ...getProtobufNetPackages(true),
            ...BuildXLSdk.getSystemMemoryPackages(true),
            ...getSystemTextJson(true),
            importFrom("System.ServiceModel.Http").pkg,
            importFrom("System.ServiceModel.Primitives").pkg,
            ...addIf(qualifier.targetFramework === "net472",
                importFrom("System.Private.ServiceModel").pkg),

            importFrom("Polly").pkg,
            importFrom("Polly.Contrib.WaitAndRetry").pkg,
        ],
        runtimeReferences: [
            importFrom("System.Private.ServiceModel").pkg
        ],
        internalsVisibleTo: [
            "BuildXL.Cache.ContentStore.Distributed.Test",
            "BuildXL.Cache.ContentStore.Distributed.Test.LongRunning",
            "BuildXL.Cache.MemoizationStore.Distributed",
            "BuildXL.Cache.MemoizationStore.Distributed.Test",
            "BuildXL.Cache.MemoizationStore.Vsts.Test",
            "BuildXL.Cache.DistributedCache.Host",
            "BuildXL.Cache.Host.Service",
        ],
        skipDocumentationGeneration: true,
        sourceGenerators: [importFrom("StructRecordGenerator").pkg],
    });
}
