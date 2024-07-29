// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as Shared from "Sdk.Managed.Shared";
import * as SysMng from "System.Management";
import * as MacServices from "BuildXL.Sandbox.MacOS";

namespace Processes {

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Processes",
        sources: globR(d`.`, "*.cs"),
        generateLogs: true,
        excludeTracing: true,
        references: [
            // IMPORTANT!!! Do not add non-bxl dependencies or any bxl projects apart from Native and Utilities.Core into this project
            //              This is consumed by MSBuild and adding any additional reference will cause it to break! BXL specific change
            //              should go to 'ProcessPipExecutor' instead.

            ...addIfLazy(!BuildXLSdk.isDotNetCore, () => [
                importFrom("System.Text.Json").pkg,
                importFrom("System.Memory").pkg,
            ]),

            ...addIf(BuildXLSdk.isFullFramework,
                BuildXLSdk.NetFx.System.IO.Compression.dll,
                BuildXLSdk.NetFx.System.Management.dll,
                NetFx.Netstandard.dll
            ),
            ...addIf(BuildXLSdk.isDotNetCoreOrStandard,
                SysMng.pkg.override<Shared.ManagedNugetPackage>({
                    runtime: [
                        Shared.Factory.createBinaryFromFiles(SysMng.Contents.all.getFile(r`runtimes/win/lib/netcoreapp2.0/System.Management.dll`))
                    ]
                }),
                importFrom("System.IO.Pipelines").pkg
            ),
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
        ],
        internalsVisibleTo: [
            "BuildXL.Engine",
            "Test.BuildXL.Engine",
            "Test.BuildXL.Processes",
            "Test.BuildXL.Processes.Detours",
            "ExternalToolTest.BuildXL.Scheduler",
            "BuildXL.ProcessPipExecutor",
        ],
        runtimeContent: [
            ...addIfLazy(Context.getCurrentHost().os === "win" && qualifier.targetRuntime === "win-x64", () => [
                importFrom("BuildXL.Sandbox.Windows").Deployment.detours,
            ]),
            ...addIfLazy(Context.getCurrentHost().os === "unix" && qualifier.targetRuntime === "linux-x64", () => [
                importFrom("BuildXL.Sandbox.Linux").Deployment.natives,
            ]),
        ],
    });
}
