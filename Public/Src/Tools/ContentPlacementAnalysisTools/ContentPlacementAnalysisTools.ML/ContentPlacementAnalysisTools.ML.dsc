// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace ContentPlacement.ML {

    export declare const qualifier: BuildXLSdk.FullFrameworkQualifier;

    const consolidateLogFile = f`cptools.ml.consolidate.exe.nlog`;
    const staticResources = {
        subfolder: "CPResources", contents : [ 
            f`CPResources\weka.jar`,
        ]};

    const scripts = {
        subfolder: "CPScripts", contents : [ 
            f`CPScripts\createDatabase.cmd`,
            f`CPScripts\linearizeDatabase.cmd`,
            f`CPScripts\buildClassifiers.cmd`,
            f`CPScripts\evaluateClassifiers.cmd`,
        ]
    };

    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "cptools.ml.consolidate",
        appConfig: f`App.Config`,
        rootNamespace: "ContentPlacementAnalysisTools.ML",
        sources: globR(d`.`, "*.cs"),
        runtimeContent: [
                consolidateLogFile,
                staticResources,
                scripts,
        ],
        references: [
            ...addIfLazy(
                BuildXLSdk.isFullFramework, () => [
                    NetFx.System.Data.dll,
                ]),
            ContentPlacement.Core.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("NLog").pkg,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("RuntimeContracts").pkg,
        ],
    });

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.ContentPlacementAnalysisTools.ML",
        sources: globR(d`.`, "*.cs"),
        references: [
            ContentPlacement.Core.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("NLog").pkg,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("RuntimeContracts").pkg,
        ],
    });

}