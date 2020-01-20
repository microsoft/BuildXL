// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
import * as ManagedSdk from "Sdk.Managed";
import { NetFx } from "Sdk.BuildXL";

namespace VfsApplication {
    export declare const qualifier: BuildXLSdk.FullFrameworkQualifier;

    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "bvfs",
        sources: globR(d`.`,"*.cs"),
        skipDocumentationGeneration: true,
        appConfig: f`App.Config`,
        references: [
            UtilitiesCore.dll,
            Grpc.dll,
            Hashing.dll,
            Library.dll,
            Interfaces.dll,
            importFrom("Microsoft.Windows.ProjFS").pkg,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Branding.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").KeyValueStore.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("Sdk.Selfhost.RocksDbSharp").pkg,

            importFrom("Grpc.Core").pkg,
            importFrom("Google.Protobuf").pkg,

            ManagedSdk.Factory.createBinary(importFrom("TransientFaultHandling.Core").Contents.all, r`lib/NET4/Microsoft.Practices.TransientFaultHandling.Core.dll`),
        ],
        runtimeContent: [

        ]
    });
}
