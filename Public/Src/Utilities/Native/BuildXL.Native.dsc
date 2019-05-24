// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import {Transformer} from "Sdk.Transformers";
import * as OsxNetCore from "runtime.osx-x64.Microsoft.NETCore.App";
import * as WinNetCore from "runtime.win-x64.Microsoft.NETCore.App";
import * as Managed from "Sdk.Managed";
import * as MacServices from "BuildXL.Sandbox.MacOS";

namespace Native {
    @@public
    export const securityDlls = BuildXLSdk.isDotNetCoreBuild ? [
        // In netCoreApp2.2 accesscontrol is missing enum: System.Security.AccessControl.AccessControlType
        importFrom("System.Security.AccessControl").pkg,
        importFrom("System.IO.FileSystem.AccessControl").pkg,

        BuildXLSdk.isTargetRuntimeOsx
            ? Managed.Factory.createBinary(importFrom("System.Security.Principal.Windows").Contents.all, r`runtimes/unix/lib/netcoreapp2.0/System.Security.Principal.Windows.dll`)
            : Managed.Factory.createBinary(importFrom("System.Security.Principal.Windows").Contents.all, r`runtimes/win/lib/netcoreapp2.0/System.Security.Principal.Windows.dll`)
    ] : [];

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Native",
        sources: globR(d`.`, "*.cs"),
        generateLogs: true,
        references: [
            $.dll,
            Interop.dll,
            ...securityDlls,
            Collections.dll,
            Configuration.dll,
        ],
        runtimeContent: [
            ...addIfLazy(MacServices.Deployment.macBinaryUsage !== "none"  && qualifier.targetRuntime === "osx-x64", () => [
                MacServices.Deployment.sandboxMonitor,
                MacServices.Deployment.ariaLibrary,
                MacServices.Deployment.interopLibrary
            ]),
        ]
    });
}
