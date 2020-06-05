// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Configuration {

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.Host.Configuration",
        sources: globR(d`.`,"*.cs"),
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Runtime.Serialization.dll,
                NetFx.System.ComponentModel.DataAnnotations.dll
            ),

            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
        ],
        skipDocumentationGeneration: true,

        allowUnsafeBlocks: false,
        
        internalsVisibleTo: [
            "BuildXL.Cache.Host.Test"
        ]
    });
}
