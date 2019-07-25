// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Configuration {

    export const references = [
        ...addIf(BuildXLSdk.isFullFramework,
            NetFx.System.Runtime.Serialization.dll
        ),

        importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
        importFrom("Newtonsoft.Json.v10").pkg,
    ];

@@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.Host.Configuration",
        sources: globR(d`.`,"*.cs"),
        references: references,
        skipDocumentationGeneration: true,
        allowUnsafeBlocks: false,
        runtimeContentToSkip: [ importFrom("Newtonsoft.Json.v10").pkg ]
    });
}
