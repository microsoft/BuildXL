// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace ContentPlacement.Extraction {

    export declare const qualifier: BuildXLSdk.FullFrameworkQualifier;

    const buildDownloaderLogFile = f`cptools.builddownloader.exe.nlog`;

    const staticResources = {
        subfolder: "CPResources", contents : [{ 
            subfolder : "Query", contents : [
                f`CPResources\Query\get_build_data.kql`,
                f`CPResources\Query\get_monthly_queue_data.kql`,
                f`CPResources\Query\get_queue_machine_map.kql`,
            ]
        }]
    };

     const scripts = {
        subfolder: "CPScripts", contents : [ 
            f`CPScripts\queueData.cmd`,
             f`CPScripts\weeklySampleDownload.cmd`,
        ]
    };

    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "cptools.builddownloader",
        appConfig: f`App.Config`,
        rootNamespace: "ContentPlacementAnalysisTools.Extraction",
        sources: globR(d`.`, "*.cs"),
        embeddedResources: [{resX: f`CPResources\constants.resx`}],
        runtimeContent: [
            staticResources,
            scripts,
            buildDownloaderLogFile
        ],
        references: [
            ...addIfLazy(
                BuildXLSdk.isFullFramework, () => [
                    NetFx.System.Data.dll,
                ]),
            ContentPlacement.Core.dll,
            importFrom("NLog").pkg,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("RuntimeContracts").pkg,
            importFrom("SharpZipLib").pkg,
            importFrom("Microsoft.Azure.Kusto.Ingest").pkg,
        ],
    });
}