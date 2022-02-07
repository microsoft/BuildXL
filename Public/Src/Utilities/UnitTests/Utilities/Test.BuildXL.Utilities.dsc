// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

namespace Core {
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.Utilities",
        allowUnsafeBlocks: true,
        sources: globR(d`.`, "*.cs"),
        runTestArgs: {
            // These tests require Detours to run itself, so we won't detour the test runner process itself
            unsafeTestRunArguments: {
                runWithUntrackedDependencies: true
            },
        },
        references: [
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Interop.dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("Newtonsoft.Json").pkg,
            ...addIf(BuildXLSdk.Flags.isMicrosoftInternal, importFrom("BuildXL.Utilities").SBOMUtilities.dll),
            ...addIf(BuildXLSdk.Flags.isMicrosoftInternal, importFrom("Microsoft.Sbom.Contracts").pkg),
            ...addIf(
                BuildXLSdk.isFullFramework,
                BuildXLSdk.withQualifier({targetFramework: qualifier.targetFramework}).NetFx.Netstandard.dll
            ),
        ],
        assemblyBindingRedirects: [
            {
                // This redirect is needed for channel-based action block tests.
                name: "System.Runtime.CompilerServices.Unsafe",
                publicKeyToken: "b03f5f7f11d50a3a",
                culture: "neutral",
                oldVersion: "0.0.0.0-5.0.0.0",
                newVersion: "5.0.0.0", // Corresponds to: { id: "System.Runtime.CompilerServices.Unsafe", version: "5.0.0.0" },
            }
        ]
    });
}
