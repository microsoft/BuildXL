// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as MSBuild from "Sdk.Selfhost.MSBuild";

namespace VBCSCompilerLogger {

    export declare const qualifier: BuildXLSdk.Net9QualifierWithNet472;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "VBCSCompilerLogger",
        skipDocumentationGeneration: true,
        skipDefaultReferences: true,
        allowUnsafeBlocks: true,
        sources: [...globR(d`.`, "*.cs"), f`../../Engine/Processes/AugmentedManifestReporter.cs`],
        defineConstants: ["VBCS_COMPILER_LOGGER"],
        references:[
            //////////////////////////////////////////////////////////////////////////////////////////////////////////////////
            // IMPORTANT: Do not add any reference to BuildXL dlls here.
            //            The primary user of VBCSCompiler logger is MSBuild.
            //            MSBuild relies on BuildXL dlls, but the versions of those dlls can be old or different from
            //            those this logger depend on. When consuming this logger, MSBuild will load the BuildXL dlls
            //            that come with its deployment, and not what this logger depends on. This can cause a runtime issue.
            //////////////////////////////////////////////////////////////////////////////////////////////////////////////////
            ...MSBuild.msbuildReferences,
            importFrom("Microsoft.CodeAnalysis.CSharp").pkg,
            importFrom("Microsoft.CodeAnalysis.VisualBasic").pkg,
            importFrom("Microsoft.CodeAnalysis.Common").pkg,
            // Roslyn API returns ImmutableArray.
            ...addIf(BuildXLSdk.isFullFramework, importFrom("System.Collections.Immutable").pkg),
            NetFx.Netstandard.dll, // due to issue https://github.com/dotnet/standard/issues/542
        ],
        runtimeContent:[
            importFrom("System.Reflection.Metadata.ForVBCS").pkg,
            importFrom("System.Runtime.CompilerServices.Unsafe").pkg,
            importFrom("System.Numerics.Vectors").pkg,
        ],
        runtimeContentToSkip: [
            importFrom("System.Collections.Immutable").pkg,
            importFrom("System.Memory").pkg,
            // Avoid deploying the standard reference in favor of the old one
            importFrom("System.Reflection.Metadata").pkg,
        ],
        internalsVisibleTo: ["Test.Tool.VBCSCompilerLogger"]
    });

    // We build here the VBCSCompiler logger with an older version
    // of the CodeAnalysis libraries in order to be able to exercise
    // legacy behavior. This is for tests only.
    @@public
    export const loggerWithOldCodeAnalysis = BuildXLSdk.library({
        assemblyName: "VBCSCompilerLoggerOldCodeAnalysis",
        skipDocumentationGeneration: true,
        allowUnsafeBlocks: true,
        sources: [...globR(d`.`, "*.cs"), f`../../Engine/Processes/AugmentedManifestReporter.cs`],
        defineConstants: ["VBCS_COMPILER_LOGGER", "TEST"],
        references:[
            //////////////////////////////////////////////////////////////////////////////////////////////////////////////////
            // IMPORTANT: Do not add any reference to BuildXL dlls here.
            //            The primary user of VBCSCompiler logger is MSBuild.
            //            MSBuild relies on BuildXL dlls, but the versions of those dlls can be old or different from
            //            those this logger depend on. When consuming this logger, MSBuild will load the BuildXL dlls
            //            that come with its deployment, and not what this logger depends on. This can cause a runtime issue.
            //////////////////////////////////////////////////////////////////////////////////////////////////////////////////
            ...MSBuild.msbuildReferences,
            importFrom("Microsoft.CodeAnalysis.CSharp.Old").pkg,
            importFrom("Microsoft.CodeAnalysis.Common.Old").pkg,
            importFrom("Microsoft.CodeAnalysis.VisualBasic.Old").pkg,
            // Roslyn API returns ImmutableArray.
            ...addIf(BuildXLSdk.isFullFramework, importFrom("System.Collections.Immutable").pkg),
        ],
        runtimeContent:[
            importFrom("System.Reflection.Metadata.ForVBCS").pkg,
        ],
        runtimeContentToSkip: [
            // Avoid deploying the standard reference in favor of the old one
            importFrom("System.Reflection.Metadata").pkg,
        ]
    });
}
