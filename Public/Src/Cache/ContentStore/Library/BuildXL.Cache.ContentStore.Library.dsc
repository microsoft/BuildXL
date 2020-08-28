// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Library {

    export declare const qualifier : BuildXLSdk.DefaultQualifierWithNetStandard20;

    @@public
    export const systemMemoryPackage = 
        BuildXLSdk.isFullFramework  ? importFrom("System.Memory").withQualifier({ targetFramework: "netstandard2.0" }).pkg : importFrom("System.Memory").pkg;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.ContentStore",
        sources: globR(d`.`,"*.cs"),
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Data.dll,
                NetFx.System.Runtime.Serialization.dll,
                NetFx.Netstandard.dll
            ),

            ...BuildXLSdk.systemThreadingTasksDataflowPackageReference,
            // Using System.Memory only for .net472, and not for net462
            ...addIf(qualifier.targetFramework !== "net462", systemMemoryPackage),
            
            ...importFrom("BuildXL.Utilities").Native.securityDlls,
            UtilitiesCore.dll,
            Hashing.dll,
            Interfaces.dll,
            Grpc.dll,
            // TODO: This needs to be renamed to just utilities... but it is in a package in public/src
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Cache.DistributedCache.Host").Configuration.dll,
            importFrom("Grpc.Core").pkg,
            importFrom("Grpc.Core.Api").pkg,
            importFrom("Google.Protobuf").pkg,
            importFrom("System.Data.SQLite.Core").pkg,
            ...BuildXLSdk.bclAsyncPackages,

            BuildXLSdk.Factory.createBinary(importFrom("TransientFaultHandling.Core").Contents.all, r`lib/NET4/Microsoft.Practices.TransientFaultHandling.Core.dll`),
            ...importFrom("BuildXL.Utilities").Native.securityDlls,
        ],
        runtimeContent: [
            importFrom("Sdk.SelfHost.Sqlite").runtimeLibs,
            importFrom("Sdk.Protocols.Grpc").runtimeContent,
        ],
        allowUnsafeBlocks: true,
        
        nullable: true,
        // Should explicitly avoiding adding a file with non-nullable attributes,
        // because this project has internals visibility into Interfaces.dll that already contains
        // such attributes.
        addNotNullAttributeFile: false,
        internalsVisibleTo: [
            "BuildXL.Cache.ContentStore.Test",
            "BuildXL.Cache.ContentStore.Distributed.Test",
            "BuildXL.Cache.ContentStore.Distributed.Test.LongRunning",
        ],
    });
}
