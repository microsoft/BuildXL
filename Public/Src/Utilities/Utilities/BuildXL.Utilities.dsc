// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as SysMng from "System.Management";
import * as Shared from "Sdk.Managed.Shared";

@@public
export const dll = BuildXLSdk.library({
    assemblyName: "BuildXL.Utilities",
    allowUnsafeBlocks: true,
    embeddedResources: [{resX: f`Strings.resx`, generatedClassMode: "implicitPublic"}],
    sources: globR(d`.`, "*.cs"),
    addPolySharpAttributes: false,
    references: [
        ...addIf(BuildXLSdk.isFullFramework,
            NetFx.System.Xml.dll,
            NetFx.System.Xml.Linq.dll,
            NetFx.System.Management.dll,
            NetFx.System.Security.dll
        ),
        Utilities.Core.dll,

        // Don't need to add the dependency for .net6+
        ...addIfLazy(qualifier.targetFramework === "netstandard2.0", () => [
            importFrom("Microsoft.Win32.Registry").pkg,
        ]),

        ...BuildXLSdk.systemThreadingChannelsPackages,

        ...addIfLazy(BuildXLSdk.isDotNetCoreOrStandard, () => [
            SysMng.pkg.override<Shared.ManagedNugetPackage>({
                    runtime: [
                        Shared.Factory.createBinaryFromFiles(SysMng.Contents.all.getFile(r`runtimes/win/lib/netcoreapp2.0/System.Management.dll`))
                    ]
            })
        ]),
        ...BuildXLSdk.tplPackages,
        importFrom("Microsoft.Bcl.AsyncInterfaces").pkg,
        importFrom("Newtonsoft.Json").pkg,
        ...BuildXLSdk.systemMemoryDeployment,
    ],
    defineConstants: qualifier.configuration === "debug" ? ["DebugStringTable"] : [],
    internalsVisibleTo: [
        "BuildXL.FrontEnd.Script",
        "BuildXL.Pips",
        "BuildXL.Scheduler",
        "Test.BuildXL.Pips",
        "Test.BuildXL.Scheduler",
        "Test.BuildXL.Scheduler.EBPF",
        "Test.BuildXL.Utilities",
        "Test.BuildXL.FrontEnd.Script",
    ],
});