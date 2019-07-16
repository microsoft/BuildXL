// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

namespace Scheduler {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Scheduler",
        generateLogs: true,
        generateLogsLite: false,
        sources: globR(d`.`, "*.cs"),
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Runtime.Serialization.dll,
                NetFx.System.Text.Encoding.dll
            ),
            Cache.dll,
            Processes.dll,
            Distribution.Grpc.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Branding.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Interop.dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").KeyValueStore.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("Sdk.Selfhost.RocksDbSharp").pkg,
        ],
        embeddedResources: [
            {
                resX: f`Filter/ErrorMessages.resx`
            }
        ],
        internalsVisibleTo: [
            "bxlanalyzer",
            "BuildXL.Engine",
            "Test.BuildXL.FingerprintStore",
            "Test.BuildXL.Scheduler",
            "Test.BuildXL.FrontEnd.MsBuild",
            "Test.Tool.Analyzers",
            "Test.Bxl",
            "IntegrationTest.BuildXL.Scheduler",
        ],
    });
}
