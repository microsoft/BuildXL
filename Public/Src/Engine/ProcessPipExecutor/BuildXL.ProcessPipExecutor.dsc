// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace ProcessPipExecutor {

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.ProcessPipExecutor",
        sources: globR(d`.`, "*.cs"),
        references: [
            ...addIfLazy(!BuildXLSdk.isDotNetCore, () => [
                importFrom("System.Memory").withQualifier({ targetFramework: "netstandard2.0" }).pkg,
            ]),

            Processes.dll,
            Processes.Remoting.dll,

            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Interop.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Plugin.dll,
            importFrom("BuildXL.Utilities").PluginGrpc.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
        ],
        internalsVisibleTo: [
            "Test.BuildXL.Processes",
            "Test.BuildXL.Processes.Detours",
            "Test.BuildXL.Scheduler",
            "ExternalToolTest.BuildXL.Scheduler",
        ]
    });
}
