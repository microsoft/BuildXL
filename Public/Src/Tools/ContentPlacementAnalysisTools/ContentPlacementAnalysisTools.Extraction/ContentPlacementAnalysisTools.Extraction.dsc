// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Tools.ContentPlacement.Extraction {

    @@public
    export const buildDownloaderLogFile = f`cptools.builddownloader.exe.nlog`;

    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "cptools.builddownloader",
        appConfig: f`App.Config`,
        rootNamespace: "ContentPlacementAnalysisTools.Extraction",
        sources: globR(d`.`, "*.cs"),
        references: [
            Tools.ContentPlacement.Core.dll,
            importFrom("NLog").pkg,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
        ],
    });
}