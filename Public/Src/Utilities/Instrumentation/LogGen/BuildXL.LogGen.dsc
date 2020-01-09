// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LogGen {
    export declare const qualifier: BuildXLSdk.TargetFrameworks.MachineQualifier.Current;

    const exe = BuildXLSdk.executable({
        assemblyName: "BuildXL.LogGen",
        platform: "anycpu32bitpreferred",
        sources: globR(d`.`, "*.cs"),
        references: [
            Common.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("BuildXL.Utilities").CodeGenerationHelper.dll,
            importFrom("Microsoft.CodeAnalysis.CSharp").pkg,
            importFrom("Microsoft.CodeAnalysis.Common").pkg,
        ],
    });

    @@public
    export const tool = BuildXLSdk.deployManagedTool({
        tool: exe,
        description: "BuildXL LogGen"
    });
}
