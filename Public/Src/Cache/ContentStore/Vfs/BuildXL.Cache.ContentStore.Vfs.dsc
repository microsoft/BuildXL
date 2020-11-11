// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
import * as ManagedSdk from "Sdk.Managed";
import { NetFx } from "Sdk.BuildXL";

namespace VfsLibrary {
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
            importFrom("Microsoft.Windows.ProjFS").pkg,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Branding.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").KeyValueStore.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            ...importFrom("Sdk.Selfhost.RocksDbSharp").pkgs,

            ...getGrpcPackages(true),
        ],
        internalsVisibleTo: [
            "BuildXL.Cache.ContentStore.Vfs.Test",
        ]
    });
}
