// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Utilities {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.FrontEnd.Utilities",
        generateLogs: false,
        sources: globR(d`.`, "*.cs"),
        references: [
            ...BuildXLSdk.tplPackages,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("Newtonsoft.Json").pkg,
            Sdk.dll,
        ],
    });
}
