// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as ContentStore from "BuildXL.Cache.ContentStore";

namespace Server {
    export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet472;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.Roxis.Server",
        sources: [
            ...globR(d`.`,"*.cs"),
        ],
        references: [
            importFrom("RuntimeContracts").pkg,

            importFrom("BuildXL.Utilities").dll,

            ContentStore.Library.dll,
            ContentStore.Interfaces.dll,
            ContentStore.Grpc.dll,

            // Needed to implement gRPC service
            Common.dll,
            Grpc.dll,
            importFrom("Grpc.Core").pkg,
            importFrom("Grpc.Core.Api").pkg,
            importFrom("Google.Protobuf").pkg,
            ...addIf(BuildXLSdk.isFullFramework,
                importFrom("System.Memory").withQualifier({targetFramework: "netstandard2.0"}).pkg
            ),
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Xml.dll,
                NetFx.System.Xml.Linq.dll,
                NetFx.System.Runtime.Serialization.dll,
                NetFx.Netstandard.dll
            ),

            // Needed to use RocksDb
            importFrom("BuildXL.Utilities").KeyValueStore.dll,
            ...importFrom("Sdk.Selfhost.RocksDbSharp").pkgs,
        ],
        internalsVisibleTo: [
            "BuildXL.Cache.Roxis.Test",
        ],
        skipDocumentationGeneration: true,
        nullable: true,
    });
}
