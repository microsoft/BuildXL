// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as MSBuild from "Sdk.Selfhost.MSBuild";
import * as Frameworks from "Sdk.Managed.Frameworks";

namespace Test.MsBuild {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.FrontEnd.MsBuild",
        sources: globR(d`.`, "*.cs"),
        // These tests are launching detours, so they cannot be run inside detours themselves
        // TODO: QTest
        testFramework: importFrom("Sdk.Managed.Testing.XUnit.UnsafeUnDetoured").framework,
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
            ...importFrom("Sdk.Selfhost.MSBuild").withQualifier(Object.merge<BuildXLSdk.DefaultQualifier>(qualifier, {targetFramework: "net472"})).deployment,
            ...importFrom("Sdk.Selfhost.MSBuild").withQualifier(Object.merge<BuildXLSdk.DefaultQualifier>(qualifier, {targetFramework: "netcoreapp3.0"})).deployment,
            {
                subfolder: "dotnet",
                contents: Frameworks.Helpers.getDotNetToolTemplate().dependencies
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
