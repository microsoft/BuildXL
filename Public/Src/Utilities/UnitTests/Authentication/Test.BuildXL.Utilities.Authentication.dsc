// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

namespace Authentication {
    @@public
    export const dll = !BuildXLSdk.Flags.isMicrosoftInternal ? undefined : BuildXLSdk.test({
        assemblyName: "Test.BuildXL.Utilities.Authentication",
        allowUnsafeBlocks: true,
        sources: globR(d`.`, "*.cs"),
        runTestArgs: {
            // These tests require Detours to run itself, so we won't detour the test runner process itself
            unsafeTestRunArguments: {
                runWithUntrackedDependencies: true
            },
        },
        references: [
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Authentication.dll,
            importFrom("Newtonsoft.Json").pkg,
        ]
    });
}
