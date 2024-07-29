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
    export const securityDlls = BuildXLSdk.isDotNetCoreOrStandard ? [
        // In netCoreApp2.2 accesscontrol is missing enum: System.Security.AccessControl.AccessControlType
        importFrom("System.IO.Pipes.AccessControl").pkg,

        importFrom("System.Threading.AccessControl").pkg,

        ...addIf(!BuildXLSdk.isDotNetCore,
            importFrom("System.Security.AccessControl").pkg,
            importFrom("System.IO.FileSystem.AccessControl").pkg
        ),

        
        ...addIf(!BuildXLSdk.isDotNetCore,
                importFrom("System.Security.Principal.Windows").pkg)
    ] : [];

    @@public
    export const nativeWin = [ 
        ...addIfLazy(qualifier.targetRuntime === "win-x64" && BuildXLSdk.isHostOsWin, () => [
            importFrom("BuildXL.Sandbox.Windows").Deployment.natives
        ])
    ];

    @@public
    export const nativeLinux = [
        ...addIfLazy(BuildXLSdk.isTargetRuntimeLinux && BuildXLSdk.isHostOsLinux, () => [
            importFrom("BuildXL.Sandbox.Linux").Deployment.natives
        ])
    ];

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Native",
        sources: globR(d`.`, "*.cs"),
        generateLogs: true,
        excludeTracing: true,
        addNotNullAttributeFile: true,
        references: [
            // IMPORTANT!!! Do not add non-bxl dependencies or any bxl projects with external dependencies into this project
            //              any non-bxl dependencies should go to BuildXL.Native.Extensions instead

            ...securityDlls,
            Utilities.Core.dll,
        ],
        runtimeContent: [
            ...nativeWin,
            ...nativeLinux,
        ],
        internalsVisibleTo: [
            "BuildXL.Native.Extensions",
            "BuildXL.Processes",
            "BuildXL.ProcessPipExecutor",
            "Test.BuildXL.Storage",
            "Test.BuildXL.Native",
        ]
    });
}
