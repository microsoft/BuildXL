// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

namespace VisualizationModel {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.VisualizationModel",
        sources: globR(d`.`, "*.cs"),
        references: [
            NetFx.System.IO.Compression.dll,
            NetFx.System.Net.Http.dll,

            Engine.dll,
            Processes.dll,
            Scheduler.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("Newtonsoft.Json").pkg,
        ],
    });
}
