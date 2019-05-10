// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LogGen {
    export declare const qualifier: BuildXLSdk.TargetFrameworks.CurrentMachineQualifier;

    const exe = BuildXLSdk.executable({
        assemblyName: "BuildXL.LogGen",
        platform: "anycpu32bitpreferred",
        sources: globR(d`.`, "*.cs"),
        references: [
            ...addIfLazy(BuildXLSdk.isFullFramework, () => [
                NetFx.System.Text.Encoding.dll,
                importFrom("Microsoft.Diagnostics.Tracing.EventSource.Redist").pkg,
                importFrom("System.Reflection.Metadata").pkg,
                importFrom("System.Collections.Immutable").pkg,
            ]),
            Common.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("BuildXL.Utilities").CodeGenerationHelper.dll,
            importFrom("Microsoft.CodeAnalysis.CSharp").withQualifier({ targetFramework: "netstandard2.0" }).pkg,
            importFrom("Microsoft.CodeAnalysis.Common").withQualifier({ targetFramework: "netstandard2.0" }).pkg,
        ],
    });

    @@public
    export const tool = BuildXLSdk.deployManagedTool({
        tool: exe,
        description: "BuildXL LogGen"
    });
}
