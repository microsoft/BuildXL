// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as GrpcSdk from "Sdk.Protocols.Grpc";

namespace Ipc.Grpc {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Ipc.Grpc",
        sources: [
            ...GrpcSdk.generateCSharp({rpc: [f`Interfaces.proto`]}).sources,
        ],
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.IO.dll
            ),
            
            ...importFrom("BuildXL.Cache.ContentStore").getGrpcPackages(true),
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
