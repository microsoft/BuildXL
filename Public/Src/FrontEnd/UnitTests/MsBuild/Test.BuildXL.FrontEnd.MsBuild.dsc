// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as MSBuild from "Sdk.Selfhost.MSBuild";

namespace Test.MsBuild {
    export declare const qualifier: MSBuild.MSBuildQualifier;

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
            ...MSBuild.msbuildRuntimeContent,
            ...MSBuild.msbuildReferences,
            {
                subfolder: a`tools`,
                contents: [{
                    subfolder: a`MsBuildGraphBuilder`,
                    contents: [
                        importFrom("BuildXL.Tools").MsBuildGraphBuilder.exe,
                    ]}
                ]
            }
        ],
        runtimeContentToSkip : [
            importFrom("System.Threading.Tasks.Dataflow").pkg
        ]
    });
}
