// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as DetoursServices from "BuildXL.Sandbox.Windows";
import * as Managed from "Sdk.Managed";
import * as Branding from "BuildXL.Branding";

namespace Factory {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.FrontEnd.Factory",
        generateLogs: true,
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Ide").Script.Debugger.dll,
            importFrom("BuildXL.Ide").VSCode.DebugProtocol.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,

            Core.dll,
            Download.dll,
            Script.dll,
            Nuget.dll,
            Sdk.dll,

            ...addIfLazy(qualifier.targetRuntime === "win-x64", () => [
                MsBuild.dll,
                Ninja.dll,
                CMake.dll,
                Rush.dll,
                JavaScript.dll,
                Yarn.dll
            ]),
        ],
        internalsVisibleTo: [
            "IntegrationTest.BuildXL.Scheduler",
            "Test.Bxl",
        ],
    });
}
