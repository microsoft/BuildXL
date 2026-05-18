// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

namespace ProcessesTestBase {

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "Test.BuildXL.ProcessesTestBase",
        sources: globR(d`.`, "*.cs"),
        // Test utility library — XML doc comments not needed
        tools: {
            csc: {
                noWarnings: [1591],
            },
        },
        // IMPORTANT: Keep this dependency closure minimal. Adding references to Scheduler, Engine,
        // or FrontEnd assemblies would pull those into net472 builds via transitive consumers,
        // defeating the purpose of this lightweight test base module.
        references: [
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("BuildXL.Utilities.UnitTests").TestProcess.exe,
            importFrom("BuildXL.Utilities.UnitTests").TestUtilities.dll,
            importFrom("BuildXL.Utilities.UnitTests").TestUtilities.XUnitV3.dll,
            ...importFrom("Sdk.Managed.Testing.XUnitV3").xunitV3References,
            ...addIf(BuildXLSdk.isFullFramework,
                BuildXLSdk.NetFx.Netstandard.dll
            ),
        ],
    });
}
