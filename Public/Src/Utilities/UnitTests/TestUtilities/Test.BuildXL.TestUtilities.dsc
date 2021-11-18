// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
import * as Managed from "Sdk.Managed";

namespace TestUtilities {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "Test.BuildXL.TestUtilities",
        sources: globR(d`.`, "*.cs"),
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Xml.dll,
                NetFx.System.Xml.Linq.dll
            ),
            ...addIf(BuildXLSdk.isDotNetCoreBuild && qualifier.targetFramework !== 'net6.0',
                BuildXLSdk.withWinRuntime(importFrom("System.Security.AccessControl").pkg, r`runtimes/win/lib/netcoreapp2.0`),
                BuildXLSdk.withWinRuntime(importFrom("System.IO.FileSystem.AccessControl").pkg, r`runtimes/win/lib/netstandard2.0`)
            ),
            ...addIf(BuildXLSdk.isTargetRuntimeOsx && !BuildXLSdk.isFullFramework && qualifier.targetFramework !== 'net6.0',
                Managed.Factory.createBinary(importFrom("System.Security.Principal.Windows").Contents.all, r`runtimes/unix/lib/netcoreapp2.0/System.Security.Principal.Windows.dll`)
            ),
            ...addIf(!BuildXLSdk.isTargetRuntimeOsx && !BuildXLSdk.isFullFramework && qualifier.targetFramework !== 'net6.0',
                Managed.Factory.createBinary(importFrom("System.Security.Principal.Windows").Contents.all, r`runtimes/win/lib/netcoreapp2.0/System.Security.Principal.Windows.dll`)
            ),
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Interop.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Engine").Processes.dll
        ]
    });
}
