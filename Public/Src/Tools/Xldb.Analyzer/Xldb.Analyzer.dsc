// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
import * as Managed from "Sdk.Managed";
import * as GrpcSdk from "Sdk.Protocols.Grpc";

namespace Xldb.Analyzer {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;
    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "xldbanalyzer",
        rootNamespace: "BuildXL.Xldb.Analyzer",
        skipDocumentationGeneration: true,
        sources: globR(d`.`, "*.cs"),
        
        references: [
            ...addIf(
                BuildXLSdk.isFullFramework,
                NetFx.Microsoft.CSharp.dll
            ),
            importFrom("Google.Protobuf").pkg,
            importFrom("Newtonsoft.Json").pkg,
            Xldb.Proto.dll,
            Xldb.dll
        ],
    });
}