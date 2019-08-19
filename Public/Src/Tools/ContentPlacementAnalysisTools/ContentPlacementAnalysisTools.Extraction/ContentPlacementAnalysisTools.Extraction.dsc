// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Tools.ContentPlacement.Extraction {

    export declare const qualifier: BuildXLSdk.FullFrameworkQualifier;

    // this is the log file config for cptools.builddownloader.exe
    @@public
    export const buildDownloaderLogFile = f`cptools.builddownloader.exe.nlog`;

    // these are the static resources i use
    @@public
    export const staticResources = {
        subfolder: "CPResources", contents : [{ 
            subfolder : "Query", contents : [
                f`CPResources\Query\get_build_data.kql`
            ]}
        ]};

    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "cptools.builddownloader",
        appConfig: f`App.Config`,
        rootNamespace: "ContentPlacementAnalysisTools.Extraction",
        sources: globR(d`.`, "*.cs"),
        embeddedResources: [{resX: f`CPResources\constants.resx`}],
        references: [
            ...addIfLazy(
                BuildXLSdk.isFullFramework, () => [
                    NetFx.System.Data.dll,
                ]),
            Tools.ContentPlacement.Core.dll,
            importFrom("NLog").pkg,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("RuntimeContracts").pkg,
            importFrom("SharpZipLib").pkg,
            importFrom("Microsoft.Azure.Kusto.Ingest").pkg,
        ],
    });
}