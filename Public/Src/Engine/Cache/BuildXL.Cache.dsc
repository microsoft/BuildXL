// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed  from "Sdk.Managed";
import * as GrpcSdk from "Sdk.Protocols.Grpc";

namespace Cache {
    @@public
    export const cacheGrpcSchemaPath: File = f`Fingerprints/Messages.proto`;

    const grpcCacheDescriptor = GrpcSdk.generateCSharp({
        proto: [cacheGrpcSchemaPath],
        includes: [GrpcSdk.includes],
    });

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Engine.Cache",
        generateLogs: true,
        sources: [
            ...grpcCacheDescriptor.sources,
            ...globR(d`.`, "*.cs"),
        ],
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.IO.dll,
                NetFx.System.Text.Encoding.dll
            ),
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Library.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Cache.VerticalStore").Interfaces.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("JsonDiffPatch.Net").pkg,
            importFrom("Newtonsoft.Json").pkg,

            ...importFrom("BuildXL.Cache.ContentStore").getProtobufPackages(), 
            
            ...BuildXLSdk.systemMemoryDeployment,
        ],
        tools: {
            csc: {
                noWarnings: [
                    1570, 1587, 1591, // Missing XML comment for publicly visible type or member: Protobuf codegen doesn't emit these.
                ]
            }
        }
    });
}
