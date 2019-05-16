// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as Shared from "Sdk.Managed.Shared";
import * as SysMng from "System.Management";

namespace Processes {
    export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet461;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Processes",

        sources: globR(d`.`, "*.cs"),
        generateLogs: true,
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                BuildXLSdk.NetFx.System.IO.Compression.dll,
                BuildXLSdk.NetFx.System.Management.dll
            ),
            ...addIf(BuildXLSdk.isDotNetCoreBuild,
                SysMng.pkg.override<Shared.ManagedNugetPackage>({
                    runtime: Context.getCurrentHost().os === "win" ? [
                        Shared.Factory.createBinaryFromFiles(SysMng.Contents.all.getFile(r`runtimes/win/lib/netcoreapp2.0/System.Management.dll`))
                    ] : []
                })
            ),
            ...importFrom("BuildXL.Utilities").Native.securityDlls,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Interop.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
        ],
        internalsVisibleTo: [
            "Test.BuildXL.Processes",
            "Test.BuildXL.Processes.Detours",
            "Test.BuildXL.Scheduler",
        ],
    });
}
