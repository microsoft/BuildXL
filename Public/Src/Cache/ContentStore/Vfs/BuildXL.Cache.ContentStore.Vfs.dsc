// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
import * as ManagedSdk from "Sdk.Managed";
import { NetFx } from "Sdk.BuildXL";

namespace VfsLibrary {
    export declare const qualifier: BuildXLSdk.DefaultQualifierWithNet472AndNetStandard20;
    
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.ContentStore.Vfs",
        sources: globR(d`.`,"*.cs"),
        skipDocumentationGeneration: true,
        references: [
            UtilitiesCore.dll,
            Grpc.dll,
            Hashing.dll,
            Library.dll,
            Interfaces.dll,
                        qualifier.targetFramework === "netstandard2.0" ?
                importFrom("Microsoft.Windows.ProjFS").withQualifier({ targetFramework: "net472" }).pkg 
                : importFrom("Microsoft.Windows.ProjFS").pkg,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Branding.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").KeyValueStore.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            ...importFrom("Sdk.Selfhost.RocksDbSharp").pkgs,

            ...getGrpcPackages(true),
            BuildXLSdk.asyncInterfacesPackage,

            importFrom("BuildXL.Cache.MemoizationStore").Library.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
        ],
        internalsVisibleTo: [
            "BuildXL.Cache.ContentStore.Vfs.Test",
        ]
    });
}
