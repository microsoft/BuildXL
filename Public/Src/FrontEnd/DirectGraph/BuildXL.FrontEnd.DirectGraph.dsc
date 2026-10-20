// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

import * as BuildXLSdk from "Sdk.BuildXL";

namespace DirectGraph {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.FrontEnd.DirectGraph",
        generateLogs: true,
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Engine").DirectGraph.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            Core.dll,
            Script.dll,
            Sdk.dll,
            Utilities.dll,
            TypeScript.Net.dll,
        ],
    });
}
