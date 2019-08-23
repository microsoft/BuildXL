// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace ContentPlacement.ML {

    export declare const qualifier: BuildXLSdk.FullFrameworkQualifier;

    const consolidateLogFile = f`cptools.ml.consolidate.exe.nlog`;
    const staticResources = {
        subfolder: "CPResources", contents : [ 
            f`CPResources\weka.jar`,
        ]};

    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "cptools.ml.consolidate",
        appConfig: f`App.Config`,
        rootNamespace: "ContentPlacementAnalysisTools.ML",
        sources: globR(d`.`, "*.cs"),
        runtimeContent: [
                consolidateLogFile,
                staticResources,
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
            importFrom("BuildXL.Utilities").Collections.dll,
        ],
    });
}