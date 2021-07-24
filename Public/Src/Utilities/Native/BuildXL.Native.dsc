// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import {Transformer} from "Sdk.Transformers";
import * as Managed from "Sdk.Managed";
import * as Shared from "Sdk.Managed.Shared";
import * as MacServices from "BuildXL.Sandbox.MacOS";

namespace Native {
    @@public
    export const securityDlls = BuildXLSdk.isDotNetCoreBuild ? [
        // In netCoreApp2.2 accesscontrol is missing enum: System.Security.AccessControl.AccessControlType
        importFrom("System.IO.Pipes.AccessControl").pkg,
        BuildXLSdk.withWinRuntime(importFrom("System.Security.AccessControl").pkg, r`runtimes/win/lib/netcoreapp2.0`),
        BuildXLSdk.withWinRuntime(importFrom("System.Threading.AccessControl").pkg, r`runtimes/win/lib/netstandard2.0`),
        BuildXLSdk.withWinRuntime(importFrom("System.IO.FileSystem.AccessControl").pkg, r`runtimes/win/lib/netstandard2.0`),

        BuildXLSdk.isTargetRuntimeOsx
            ? Managed.Factory.createBinary(importFrom("System.Security.Principal.Windows").Contents.all, r`runtimes/unix/lib/netcoreapp2.0/System.Security.Principal.Windows.dll`)
            : Managed.Factory.createBinary(importFrom("System.Security.Principal.Windows").Contents.all, r`runtimes/win/lib/netcoreapp2.0/System.Security.Principal.Windows.dll`)
    ] : [];

    @@public
    export const nativeWin = [ 
        ...addIfLazy(qualifier.targetRuntime === "win-x64" && Context.getCurrentHost().os === "win", () => [
            importFrom("BuildXL.Sandbox.Windows").Deployment.natives
        ])
    ];

    @@public
    export const nativeMac = [
        ...addIfLazy(MacServices.Deployment.macBinaryUsage !== "none" && qualifier.targetRuntime === "osx-x64", () =>
        [
            MacServices.Deployment.interopLibrary,
        ]),
    ];

    @@public
    export const nativeLinux = [
        ...addIfLazy(qualifier.targetRuntime === "linux-x64", () =>
        [
            ...globR(d`${importFrom("runtime.linux-x64.BuildXL").Contents.all.root}/runtimes/linux-x64/native/${qualifier.configuration}`, "*.so")
        ]),
    ];

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
            ...nativeMac,
            ...nativeWin,
            ...nativeLinux,
        ],
        internalsVisibleTo: [
            "BuildXL.Processes",
            "Test.BuildXL.Storage"
        ]
    });
}
