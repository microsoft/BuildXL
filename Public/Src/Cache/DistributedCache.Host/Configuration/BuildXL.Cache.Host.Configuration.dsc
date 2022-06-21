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
                NetFx.System.ComponentModel.DataAnnotations.dll,
                NetFx.System.Web.dll
            ),
            ...addIf(qualifier.targetFramework === "netstandard2.0",
                NetFx.System.ComponentModel.DataAnnotations.dll
            ),

            ...importFrom("BuildXL.Cache.ContentStore").getSystemTextJson(true),
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Grpc.dll,
        ],
        skipDocumentationGeneration: true,

        allowUnsafeBlocks: false,
        nullable: true,
        
        internalsVisibleTo: [
            "BuildXL.Cache.Host.Test"
        ]
    });
}
