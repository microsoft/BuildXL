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
            ...addIfLazy(BuildXLSdk.isDotNetCoreOrStandard, () => [
                  BuildXLSdk.withWinRuntime(importFrom("System.Security.Cryptography.ProtectedData").pkg, r`runtimes/win/lib/netstandard2.0`),
            ]),

            ...addIfLazy(qualifier.targetFramework === "netstandard2.0", () => [
                // Don't need adding the following package for net6+
                BuildXLSdk.withWinRuntime(importFrom("System.Security.Cryptography.Cng").pkg, r`runtimes/win/lib/netstandard2.0`),
          ]),

            ...addIf(!BuildXLSdk.isDotNetCoreOrStandard, NetFx.System.Security.dll),
            Interfaces.dll,
            importFrom("Newtonsoft.Json").pkg,
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
