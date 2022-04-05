// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
import * as ManagedSdk from "Sdk.Managed";
import { NetFx } from "Sdk.BuildXL";

namespace VfsApplication {
    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "bvfs",
        sources: globR(d`.`,"*.cs"),
        skipDocumentationGeneration: true,
        appConfig: f`App.config`,
        references: [
            VfsLibrary.dll,
            UtilitiesCore.dll,
            Hashing.dll,
            Library.dll,
            Interfaces.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Branding.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
        ],
        runtimeContent: [

        ]
    });
}
