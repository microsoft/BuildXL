// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as GrpcSdk from "Sdk.Protocols.Grpc";

namespace Grpc {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.ContentStore.Grpc",
        sources: [
            ...globR(d`.`, "*.cs"),
            ...GrpcSdk.generateCSharp({rpc: [f`ContentStore.proto`]}).sources,
        ],
        references: [
            ...getGrpcPackages(true),
            ...BuildXLSdk.bclAsyncPackages,
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
