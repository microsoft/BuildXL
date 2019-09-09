// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace ContentPlacement.Core {
    
    export declare const qualifier: BuildXLSdk.FullFrameworkQualifier;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.ContentPlacementAnalysisTools.Core",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("Newtonsoft.Json").pkg,
            importFrom("NLog").pkg,
            importFrom("RuntimeContracts").pkg,

            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
        ],
        internalsVisibleTo: [
            "bxlanalyzer",
            "cptools.builddownloader",
            "cptools.ml.consolidate",
        ],
    });
}
