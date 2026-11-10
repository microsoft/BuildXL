// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace SBOMUtilities {
    // Restrict SBOM utilities to the supported .NET target frameworks
    // so we are not trying to build SBOMUtilities for other qualifiers.
    export declare const qualifier: {
        configuration: "debug" | "release";
        targetFramework: "net8.0" | "net9.0" | "net10.0" | "net11.0";
        targetRuntime: "win-x64" | "osx-x64" | "osx-arm64" | "linux-x64";
    };

    @@public
    export const dll = !BuildXLSdk.Flags.isMicrosoftInternal ? undefined : BuildXLSdk.library({
        assemblyName: "BuildXL.Utilities.SBOMUtilities",
        sources: globR(d`.`, "*.cs"),
        references: [
            ...addIf(
                BuildXLSdk.isFullFramework,
                BuildXLSdk.withQualifier({targetFramework: "net472"}).NetFx.Netstandard.dll
            ),
            importFrom("Newtonsoft.Json").pkg,
            importFrom("Microsoft.Sbom.Contracts").pkg,
        ],
        internalsVisibleTo: [
            "Test.BuildXL.Utilities",
        ],
    });
}
