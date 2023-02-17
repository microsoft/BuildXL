// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Utilities.Core {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Utilities.Core",
        allowUnsafeBlocks: true,
        sources: globR(d`.`, "*.cs"),
        addNotNullAttributeFile: true,
        addCallerArgumentExpressionAttribute: false,
        references: [
            // IMPORTANT!!! Do not add non-bxl dependencies into this project, any non-bxl dependencies should go to BuildXL.Utilities instead

            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Xml.dll
            ),
            Collections.dll,
            Interop.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Common.dll,

            ...BuildXLSdk.tplPackages,
            ...BuildXLSdk.systemMemoryDeployment,
        ],
        internalsVisibleTo: [
            "BuildXL.FrontEnd.Script",
            "BuildXL.Pips",
            "BuildXL.Scheduler",
            "BuildXL.Utilities",
            "Test.BuildXL.Pips",
            "Test.BuildXL.Scheduler",
            "Test.BuildXL.Utilities",
            "Test.BuildXL.FrontEnd.Script",
        ],
    });
}