// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
import * as Managed from "Sdk.Managed";
import * as GrpcSdk from "Sdk.Protocols.Grpc";

namespace Xldb {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Xldb",
        skipDocumentationGeneration: true,
        sources: globR(d`.`, "*.cs"),
        
        references: [
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").KeyValueStore.dll,
            importFrom("Google.Protobuf").pkg,
            Xldb.Proto.dll,
        ],
    });
}