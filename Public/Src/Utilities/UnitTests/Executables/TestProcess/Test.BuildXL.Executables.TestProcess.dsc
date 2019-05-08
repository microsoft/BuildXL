// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as MacServices from "BuildXL.Sandbox.MacOS";
import * as ManagedSdk from "Sdk.Managed";

namespace TestProcess {
    const exeArgs = <BuildXLSdk.Arguments>{
        assemblyName: "Test.BuildXL.Executables.TestProcess",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Interop.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
        ],
    };

    @@public
    export const exe = BuildXLSdk.executable(exeArgs);

    @@public
    export const nativeExe = BuildXLSdk.nativeExecutable(exeArgs);

    @@public
    export const deploymentDefinition: Deployment.Definition = {
        contents: [
            qualifier.targetRuntime === "win-x64"
                ? {
                    subfolder: r`TestProcess/Win`,
                    contents: [
                        $.withQualifier({
                            configuration: qualifier.configuration,
                            targetFramework: "net472",
                            targetRuntime: "win-x64"
                        }).testProcessExe
                    ]
                }
                : {
                    subfolder: r`TestProcess/MacOs`,
                    contents: [
                        $.withQualifier({
                            configuration: qualifier.configuration,
                            targetFramework: "netcoreapp2.2",
                            targetRuntime: "osx-x64"
                        }).TestProcess.nativeExe,

                        $.withQualifier({
                            configuration: qualifier.configuration,
                            targetFramework: "netcoreapp2.2",
                            targetRuntime: "osx-x64"
                        }).TestProcess.exe.runtime.binary, // this .dll is needed for libraries that compile against it

                        ...addIfLazy(MacServices.Deployment.macBinaryUsage !== "none", () => [
                            MacServices.Deployment.coreDumpTester
                        ]),
                    ]
                }
        ]
    };
}
