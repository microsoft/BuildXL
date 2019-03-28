// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";

namespace TypeScript.Net.UnitTests {
    @@public
    export const testDll = BuildXLSdk.test({
        assemblyName: "Test.TypeScript.Net",
        rootNamespace: "TypeScript.Net.UnitTests",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Utilities").dll,
            Script.dll,
            TypeScript.Net.dll,
            Sdk.dll,
            ...addIf(BuildXLSdk.isFullFramework,
                importFrom("System.Collections.Immutable").pkg
            ),
        ],
        runtimeContent: [
            {
                subfolder: a`Cases`,
                contents: glob(d`Cases`),
            },
            {
                subfolder: a`FailingCases`,
                contents: glob(d`FailingCases`),
            },
            {
                subfolder: a`CrashingCases`,
                contents: glob(d`CrashingCases`),
            },
            {
                subfolder: a`Libs`,
                contents: glob(d`Libs`),
            },
        ],
    });
}
