// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace ProcessPipExecutor {

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.ProcessPipExecutor",
        sources: globR(d`.`, "*.cs"),
        allowUnsafeBlocks: true,
        references: [
            ...addIfLazy(!BuildXLSdk.isDotNetCore, () => [
                importFrom("System.Memory").pkg,
            ]),

            Processes.dll,
            Processes.External.dll,

            ...importFrom("BuildXL.Utilities").Native.securityDlls,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Plugin.dll,
            importFrom("BuildXL.Utilities").PluginGrpc.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
        ],
        internalsVisibleTo: [
            "Test.BuildXL.Processes",
            "Test.BuildXL.Processes.EBPF",
            "Test.BuildXL.Processes.Detours",
            "Test.BuildXL.Scheduler",
            "Test.BuildXL.Scheduler.EBPF",
            "ExternalToolTest.BuildXL.Scheduler",
            "IntegrationTest.BuildXL.Scheduler",
            "IntegrationTest.BuildXL.Scheduler.EBPF",
        ]
    });
}
