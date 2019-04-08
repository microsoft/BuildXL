// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace MimicGenerator {
    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "MimicGenerator",
        rootNamespace: "Tool.MimicGenerator",
        skipDocumentationGeneration: true,
        sources: globR(d`.`, "*.cs"),
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.IO.dll
            ),
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Branding.dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("Newtonsoft.Json").pkg,
        ],
        embeddedResources: [
            {
                 linkedContent: [
                     f`Content/CacheConfig.json`
                ]
            }
        ],
    });
}
