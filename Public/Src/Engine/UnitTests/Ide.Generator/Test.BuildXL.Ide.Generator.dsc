// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Ide.Generator {
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.Ide.Generator",
        sources: globR(d`.`, "*.cs"),
        references: [
            EngineTestUtilities.dll,
            importFrom("BuildXL.Ide").Generator.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Branding.dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
        ],
    });
}
