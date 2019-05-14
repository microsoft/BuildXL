// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as MacServices from "BuildXL.Sandbox.MacOS";

namespace TestProcess {
    @@public
    export const exe = BuildXLSdk.nativeExecutable({
        assemblyName: "Test.BuildXL.Executables.TestProcess",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Interop.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
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
                            targetFramework: "netcoreapp3.0",
                            targetRuntime: "osx-x64"
                        }).testProcessExe,

                        ...addIfLazy(MacServices.Deployment.macBinaryUsage !== "none", () => [
                            MacServices.Deployment.coreDumpTester
                        ]),
                    ]
                }
        ]
    };
}
