// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import {Transformer} from "Sdk.Transformers";
import * as Managed from "Sdk.Managed";
import * as Shared from "Sdk.Managed.Shared";
import * as MacServices from "BuildXL.Sandbox.MacOS";
import { Sandbox as LinuxSandbox } from "BuildXL.Sandbox.Linux";

namespace Native {
    @@public
    export const securityDlls = BuildXLSdk.isDotNetCoreBuild ? [
        // In netCoreApp2.2 accesscontrol is missing enum: System.Security.AccessControl.AccessControlType
        importFrom("System.IO.Pipes.AccessControl").pkg,
        
        BuildXLSdk.withWinRuntime(importFrom("System.Threading.AccessControl").pkg, r`runtimes/win/lib/netstandard2.0`),

        ...addIf(qualifier.targetFramework !== 'net6.0', 
            BuildXLSdk.withWinRuntime(importFrom("System.Security.AccessControl").pkg, r`runtimes/win/lib/netcoreapp2.0`),
            BuildXLSdk.withWinRuntime(importFrom("System.IO.FileSystem.AccessControl").pkg, r`runtimes/win/lib/netstandard2.0`)
        ),

        ...addIf(qualifier.targetFramework === 'net6.0',
            BuildXLSdk.withWinRuntime(importFrom("System.IO.FileSystem.AccessControl.v6.0.0").pkg, r`runtimes/win/lib/netstandard2.0`),
            BuildXLSdk.withWinRuntime(importFrom("System.Security.AccessControl.v6.0.0").pkg, r`runtimes/win/lib/netstandard2.0`),
            BuildXLSdk.withWinRuntime(importFrom("System.Security.Principal.Windows.v6.0.0").pkg, r`runtimes/win/lib/netstandard2.0`)
        ),
        
        ...addIf(qualifier.targetFramework !== 'net6.0', 
            BuildXLSdk.isTargetRuntimeOsx
                ? Managed.Factory.createBinary(importFrom("System.Security.Principal.Windows").Contents.all, r`runtimes/unix/lib/netcoreapp2.0/System.Security.Principal.Windows.dll`)
                : Managed.Factory.createBinary(importFrom("System.Security.Principal.Windows").Contents.all, r`runtimes/win/lib/netcoreapp2.0/System.Security.Principal.Windows.dll`))
    ] : [];

    @@public
    export const nativeWin = [ 
        ...addIfLazy(qualifier.targetRuntime === "win-x64" && BuildXLSdk.isHostOsWin, () => [
            importFrom("BuildXL.Sandbox.Windows").Deployment.natives
        ])
    ];

    @@public
    export const nativeMac = [
        ...addIfLazy(MacServices.Deployment.macBinaryUsage !== "none" && BuildXLSdk.isTargetRuntimeOsx, () =>
        [
            MacServices.Deployment.interopLibrary,
        ]),
    ];

    @@public
    export const nativeLinux = [
        ...addIfLazy(BuildXLSdk.isTargetRuntimeLinux, () => BuildXLSdk.isHostOsLinux && !BuildXLSdk.Flags.isMicrosoftInternal
            ? [ LinuxSandbox.libBxlUtils, LinuxSandbox.bxlEnv, LinuxSandbox.libBxlAudit, LinuxSandbox.libDetours ]
            : importFrom("runtime.linux-x64.BuildXL").Contents.all.ensureContents({subFolder: r`runtimes/linux-x64/native/${qualifier.configuration}`}).getContent())
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
            "Test.BuildXL.Storage",
			"Test.BuildXL.Native",
        ]
    });
}
