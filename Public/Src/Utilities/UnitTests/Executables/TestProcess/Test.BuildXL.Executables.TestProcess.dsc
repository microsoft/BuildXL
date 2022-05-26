// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as MacServices from "BuildXL.Sandbox.MacOS";

namespace TestProcess {
    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "Test.BuildXL.Executables.TestProcess",
        sources: globR(d`.`, "*.cs"),
        defineConstants: [ "TestProcess" ],
        references: [
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Interop.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Engine").Processes.dll,
        ]
    });

    @@public
    export const deploymentDefinition: Deployment.Definition = {
        contents: [
            qualifier.targetRuntime === "win-x64"
                ? {
                    subfolder: r`TestProcess/Win`,
                    contents: [
                        $.withQualifier({
                            targetFramework: "net472",
                            targetRuntime: "win-x64"
                        }).testProcessExe
                    ]
                }
                :
            qualifier.targetRuntime === "osx-x64"
                ? {
                    subfolder: r`TestProcess/MacOs`,
                    contents: [
                        $.withQualifier({
                            targetFramework: "net6.0",
                            targetRuntime: "osx-x64"
                        }).testProcessExe,

                        ...addIfLazy(MacServices.Deployment.macBinaryUsage !== "none", () => [
                            MacServices.Deployment.coreDumpTester
                        ]),
                    ]
                }
                :
            qualifier.targetRuntime === "linux-x64"
                ? {
                    subfolder: r`TestProcess/Unix`,
                    contents: [
                        $.withQualifier({
                            targetFramework: "net6.0",
                            targetRuntime: "linux-x64"
                        }).testProcessExe,
                    ]
                }
                : Contract.fail("Unknown target runtime: " + qualifier.targetRuntime)
        ]
    };
}