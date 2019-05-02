// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

namespace Configuration {

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.Host.Configuration",
        sources: globR(d`.`,"*.cs"),
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Runtime.Serialization.dll
            ),

            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,

            ...addIf(BuildXLSdk.isFullFramework,
                importFrom("Newtonsoft.Json").pkg
            ),

            ...addIf(BuildXLSdk.isDotNetCoreBuild,
                Managed.Factory.createBinary(importFrom("Newtonsoft.Json").Contents.all, r`lib/netstandard2.0/Newtonsoft.Json.dll`)
            ),
        ],
        skipDocumentationGeneration: true,

        allowUnsafeBlocks: false
    });
}
