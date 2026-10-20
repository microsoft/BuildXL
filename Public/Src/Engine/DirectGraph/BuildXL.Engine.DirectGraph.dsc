// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

import * as GrpcSdk from "Sdk.Protocols.Grpc";

namespace DirectGraph {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Engine.DirectGraph",
        generateLogs: true,
        sources: [
            ...globR(d`.`, "*.cs"),
            ...GrpcSdk.generateCSharp({
                rpc: [f`DirectGraph.proto`],
            }).sources,
        ],
        references: [
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("BuildXL.Utilities.Instrumentation").AriaCommon.dll,
            ...importFrom("BuildXL.Cache.ContentStore").getGrpcPackages(true),
        ],
        tools: {
            csc: {
                noWarnings: [
                    1570, 1587, 1591,
                ]
            }
        }
    });
}
