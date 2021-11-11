// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Core {
    export declare const qualifier: BuildXLSdk.NetStandardQualifier;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.LogGen.Core",
        sources: globR(d`.`, "*.cs"),
        references: [
            Common.dll,
            importFrom("BuildXL.Utilities").CodeGenerationHelper.dll,
            importFrom("Microsoft.CodeAnalysis.CSharp").pkg,
            importFrom("Microsoft.CodeAnalysis.Common").pkg,
            importFrom("System.Collections.Immutable.ForVBCS").pkg,
        ],
    });
}