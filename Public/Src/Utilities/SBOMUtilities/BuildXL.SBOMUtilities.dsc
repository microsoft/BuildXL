// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace SBOMUtilities {
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
            importFrom("Microsoft.SBOMApi").pkg,
            // TODO: Uncomment this and remove SBOMApi once newer versions are stable
            //importFrom("Microsoft.Sbom.Contracts").pkg,
        ],
        internalsVisibleTo: [
            "Test.BuildXL.Utilities",
        ],
        runtimeContent: [
            importFrom("BuildXL.Tools").SBOMConverter.withQualifier({
                configuration: qualifier.configuration,
                targetFramework: "net6.0",
                targetRuntime: qualifier.targetRuntime
            }).deployment
        ]
    });
}
