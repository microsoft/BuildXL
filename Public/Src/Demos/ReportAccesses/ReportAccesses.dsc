// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace ReportAccesses {
    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "ReportAccesses",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
        ],
    });
}
