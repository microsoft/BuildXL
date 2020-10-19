// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as GrpcSdk from "Sdk.Protocols.Grpc";

namespace PluginGrpc {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Plugin.Grpc",
        sources: [
            ...GrpcSdk.generateCSharp({
                proto: [f`Messages.proto`],
                rpc: [f`Interfaces.proto`]
            }).sources,
        ],
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.IO.dll,
                NetFx.Netstandard.dll
            ),
            ...importFrom("BuildXL.Cache.ContentStore").getGrpcPackages(false),
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
