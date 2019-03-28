// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LanguageService.Protocol {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "LanguageServer",
        sources: globR(d`.`, "*.cs"),
        skipAssemblySigning: true,
        references: [
            ...addIfLazy(BuildXLSdk.isFullFramework, () => [
                NetFx.System.IO.dll,
                NetFx.System.Runtime.Serialization.dll,
            ]),
            importFrom("StreamJsonRpc").pkg,
            importFrom("Newtonsoft.Json").pkg,
        ],
    });
}
