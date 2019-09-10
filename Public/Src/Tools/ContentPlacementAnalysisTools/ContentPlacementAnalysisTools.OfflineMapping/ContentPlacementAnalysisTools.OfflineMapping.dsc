// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace ContentPlacement.OfflineMapping {

    export declare const qualifier: BuildXLSdk.FullFrameworkQualifier;

    const offlineMappingLogFile = f`cptools.ml.offlineMapping.exe.nlog`;

    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "cptools.ml.offlineMapping",
        appConfig: f`App.Config`,
        rootNamespace: "ContentPlacementAnalysisTools.OfflineMapping",
        sources: globR(d`.`, "*.cs"),
        runtimeContent: [
            offlineMappingLogFile,
        ],
        references: [
            ...addIfLazy(
                BuildXLSdk.isFullFramework, () => [
                    NetFx.System.Data.dll,
                ]),
            ContentPlacement.Core.dll,
            ContentPlacement.ML.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Library.dll,
            importFrom("BuildXL.Cache.ContentStore").Distributed.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("NLog").pkg,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("RuntimeContracts").pkg,
            importFrom("SharpZipLib").pkg,
        ],
    });
}