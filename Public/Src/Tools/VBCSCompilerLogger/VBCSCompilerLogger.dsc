// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as MSBuild from "Sdk.Selfhost.MSBuild";

namespace VBCSCompilerLogger {

    export declare const qualifier: BuildXLSdk.Net7QualifierWithNet472;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "VBCSCompilerLogger",
        skipDocumentationGeneration: true,
        skipDefaultReferences: true,
        sources: globR(d`.`, "*.cs"),
        references:[
            ...MSBuild.msbuildReferences,
            importFrom("Microsoft.CodeAnalysis.CSharp.ForVBCS").pkg,
            importFrom("Microsoft.CodeAnalysis.VisualBasic.ForVBCS").pkg,
            importFrom("Microsoft.CodeAnalysis.Common.ForVBCS").pkg,
            ...addIf(BuildXLSdk.isFullFramework, importFrom("System.Collections.Immutable").pkg),
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            NetFx.Netstandard.dll, // due to issue https://github.com/dotnet/standard/issues/542
        ],
        runtimeContent:[
            importFrom("System.Reflection.Metadata").pkg,
            importFrom("System.Memory").pkg,
            importFrom("System.Runtime.CompilerServices.Unsafe").pkg,
            importFrom("System.Numerics.Vectors").pkg,
        ],
        runtimeContentToSkip: [
            importFrom("System.Collections.Immutable").pkg,
            importFrom("System.Memory").pkg,
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
        sources: globR(d`.`, "*.cs"),
        references:[
            ...MSBuild.msbuildReferences,
            importFrom("Microsoft.CodeAnalysis.CSharp.Old").pkg,
            importFrom("Microsoft.CodeAnalysis.Common.Old").pkg,
            importFrom("Microsoft.CodeAnalysis.VisualBasic.Old").pkg,
            ...addIf(BuildXLSdk.isFullFramework, importFrom("System.Collections.Immutable").pkg),
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Engine").Processes.dll,
        ],
        runtimeContent:[
            importFrom("System.Reflection.Metadata").pkg
        ],
        defineConstants: ["TEST"]
    });
}
