// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Tools.ContentPlacement.Core {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.ContentPlacementAnalysisTools.Core",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("Newtonsoft.Json").pkg,
        ],
        internalsVisibleTo: [
            "bxlanalyzer",
            "cptools.builddownloader"
        ],
    });
}
