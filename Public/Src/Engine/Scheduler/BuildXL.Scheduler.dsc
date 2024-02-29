// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

namespace Scheduler {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Scheduler",
        generateLogs: true,
        sources: globR(d`.`, "*.cs"),
        addPolySharpAttributes: false,
        references: [
            ...addIfLazy(BuildXLSdk.isFullFramework, () => [
                NetFx.System.Runtime.Serialization.dll,
                NetFx.System.Text.Encoding.dll,
                NetFx.Netstandard.dll,
                importFrom("System.Text.Json").pkg,
                importFrom("System.Collections.Immutable").pkg,
                importFrom("System.Threading.Tasks.Extensions").pkg
            ]),
            ...addIfLazy(BuildXLSdk.isDotNetCore, () => [
                importFrom("BuildXL.Utilities").PackedTable.dll,
                importFrom("BuildXL.Utilities").PackedExecution.dll
            ]),
            Cache.dll,
            ProcessPipExecutor.dll,
            Processes.dll,
            Processes.External.dll,
            ProcessPipExecutor.dll,
            Distribution.Grpc.dll,
            ViewModel.dll,
            importFrom("BuildXL.Cache.ContentStore").Library.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Branding.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").Ipc.Providers.dll,
            importFrom("BuildXL.Utilities").Plugin.dll,
            importFrom("BuildXL.Utilities").KeyValueStore.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Native.Extensions.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("BuildXL.Utilities.Instrumentation").AriaCommon.dll,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            importFrom("Newtonsoft.Json").pkg,
            ...importFrom("Sdk.Selfhost.RocksDbSharp").pkgs,
            ...importFrom("BuildXL.Cache.ContentStore").getProtobufPackages(),
            
            ...BuildXLSdk.systemMemoryDeployment,
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
            "Test.BuildXL.Distribution",
        ],
    });
}
