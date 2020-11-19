// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as MSBuild from "Sdk.Selfhost.MSBuild";
import * as Frameworks from "Sdk.Managed.Frameworks";

namespace Test.MsBuild {
    @@public
    export const dll = BuildXLSdk.test({
        runTestArgs: {
            unsafeTestRunArguments: {
                // These tests require Detours to run itself, so we won't detour the test runner process itself
                runWithUntrackedDependencies: true
            },
        },
        assemblyName: "Test.BuildXL.FrontEnd.MsBuild",
        sources: globR(d`.`, "*.cs"),
        references: [
            Script.dll,
            Core.dll,
            importFrom("BuildXL.Core.UnitTests").EngineTestUtilities.dll,
            importFrom("BuildXL.Engine").Engine.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.FrontEnd").Core.dll,
            importFrom("BuildXL.FrontEnd").Download.dll,
            importFrom("BuildXL.FrontEnd").MsBuild.dll,
            importFrom("BuildXL.FrontEnd").MsBuild.Serialization.dll,
            importFrom("BuildXL.FrontEnd").Nuget.dll,
            importFrom("BuildXL.FrontEnd").Script.dll,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            importFrom("BuildXL.FrontEnd").TypeScript.Net.dll,
            importFrom("BuildXL.FrontEnd").Utilities.dll,
            importFrom("BuildXL.FrontEnd").SdkProjectGraph.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
            ...BuildXLSdk.tplPackages,
        ],
        
        runtimeContent: [
            // We need both the full framework and dotnet core versions of MSBuild, plus dotnet.exe for the dotnet core case
            ...importFrom("Sdk.Selfhost.MSBuild").withQualifier({targetFramework: "net472"}).deployment,
            ...importFrom("Sdk.Selfhost.MSBuild").withQualifier({targetFramework: "netcoreapp3.1"}).deployment,
            {
                subfolder: "dotnet",
                contents: Frameworks.Helpers.getDotNetToolTemplate(qualifier.targetFramework === "net5.0").dependencies
            },
            {
                subfolder: a`tools`,
                contents: [importFrom("BuildXL.Tools").MsBuildGraphBuilder.deployment]
            },
            // We need csc.exe for integration tests
            {
                subfolder: a`Compilers`,
                contents: [
                    {
                        subfolder: a`net472`,
                        contents: [importFrom("Microsoft.Net.Compilers").Contents.all]
                    },
                    {
                        subfolder: a`dotnetcore`,
                        contents: [importFrom("Microsoft.NETCore.Compilers").Contents.all]
                    },
                ]
            }
        ],
    });
}
