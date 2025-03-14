// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace SBOMUtilities {
    // SBOM packages are only available for net8.0 and net9.0. Need to restrict the targetFramework here
    // so we are not trying to build SBOMUtilities for other qualifiers.
    export declare const qualifier: {
        configuration: "debug" | "release";
        targetFramework: "net8.0" | "net9.0";
        targetRuntime: "win-x64" | "osx-x64" | "linux-x64";
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
