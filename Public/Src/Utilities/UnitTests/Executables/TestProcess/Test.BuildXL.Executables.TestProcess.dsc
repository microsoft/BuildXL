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
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("BuildXL.Engine").Processes.dll,
        ]
    });

    @@public
    export const deploymentDefinition: Deployment.Definition = {
        contents: getDeploymentContents()
    };

    export function getDeploymentContents() : Deployment.DeployableItem[] {
        switch (qualifier.targetRuntime) {
            case "win-x64":
                return [
                    {
                        subfolder: r`TestProcess/Win`,
                        contents: [
                            $.withQualifier({
                                targetFramework: "net9.0",
                                targetRuntime: "win-x64"
                            }).testProcessExe
                        ]
                    }
                ];
            case "osx-x64":
                return [];
            case "linux-x64":
                return [
                    {
                        subfolder: r`TestProcess/Unix`,
                        contents: [
                            $.withQualifier({
                                targetFramework: "net9.0",
                                targetRuntime: "linux-x64"
                            }).testProcessExe,
                            // CODESYNC: Public\Src\Utilities\UnitTests\Executables\TestProcess\Operation.cs
                            VFork.withQualifier({
                                targetFramework: "net9.0",
                                targetRuntime: "linux-x64"
                            }).exe
                        ]
                    }
                ];
            default:
            Contract.fail("Unknown target runtime: " + qualifier.targetRuntime);
        }
    }
}