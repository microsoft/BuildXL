// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as DetoursServices from "BuildXL.Sandbox.Windows";
import * as Managed from "Sdk.Managed";
import * as Branding from "BuildXL.Branding";

namespace Factory {
    export declare const qualifier : BuildXLSdk.DefaultQualifier;
    
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.FrontEnd.Factory",
        generateLogs: true,
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Ide").Script.Debugger.dll,
            importFrom("BuildXL.Ide").VSCode.DebugProtocol.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,

            Core.dll,
            Download.dll,
            Script.dll,
            Nuget.dll,
            Sdk.dll,
            Rush.dll,
            Yarn.dll,
            JavaScript.dll,
            Lage.dll,
            Ninja.dll,
            Nx.dll,
            ...addIfLazy(qualifier.targetRuntime === "win-x64", () => [            
                MsBuild.dll,               
            ]),
        ],
        internalsVisibleTo: [
            "IntegrationTest.BuildXL.Scheduler",
            "Test.Bxl",
        ],
    });
}
