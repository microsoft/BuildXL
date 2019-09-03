
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
import * as Managed from "Sdk.Managed";
import * as GrpcSdk from "Sdk.Protocols.Grpc";

namespace Xldb.Proto {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Xldb.Proto",
        skipDocumentationGeneration: true,
        sources: GrpcSdk.generate({
            proto: globR(d`.`, "*.proto"), 
            includes: [importFrom("Google.Protobuf.Tools").Contents.all]
        }).sources,
        references: [importFrom("Google.Protobuf").pkg],
    });
}