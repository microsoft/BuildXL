// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as GrpcSdk from "Sdk.Protocols.Grpc";

namespace Grpc {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.ContentStore.Grpc",
        sources: [
            ...globR(d`.`, "*.cs"),
            ...GrpcSdk.generate({rpc: [f`ContentStore.proto`]}).sources,
        ],
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.IO.dll
            ),
            
            importFrom("Grpc.Core").pkg,
            importFrom("Google.Protobuf").pkg,
            importFrom("System.Interactive.Async").pkg,
        ],
        tools: {
            csc: {
                noWarnings: [
                    1591, // Missing XML comment for publicly visible type or member: Protobuf codegen doesn't emit these.
                ]
            }
        }
    });
}
