// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as BuildXLSdk from "Sdk.BuildXL";

namespace Test.Ninja {
    @@public
    export const dll = BuildXLSdk.test({
        runTestArgs: {
            unsafeTestRunArguments: {
                // These tests require Detours to run itself, so we won't detour the test runner process itself
                runWithUntrackedDependencies: true
            },
        },
        assemblyName: "Test.BuildXL.FrontEnd.Ninja",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,
            importFrom("BuildXL.FrontEnd").Core.dll,
            importFrom("BuildXL.FrontEnd").Nuget.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
            importFrom("BuildXL.Engine").Engine.dll,
            importFrom("BuildXL.Core.UnitTests").EngineTestUtilities.dll,
            importFrom("BuildXL.Utilities.UnitTests").TestUtilities.dll,
            importFrom("BuildXL.Utilities.UnitTests").TestUtilities.XUnit.dll,
            importFrom("BuildXL.FrontEnd").Core.dll,
            importFrom("BuildXL.FrontEnd").Ninja.dll,
            importFrom("BuildXL.FrontEnd").Ninja.Serialization.dll,
            importFrom("BuildXL.FrontEnd").Script.dll,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            importFrom("BuildXL.FrontEnd").TypeScript.Net.dll,
            Script.dll,
            Core.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            ...BuildXLSdk.tplPackages,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.Pips").dll,
        ],
        runtimeContent: [
            {
                subfolder: a`tools`,
                contents: [
                    {
                        subfolder: a`CMakeNinja`,
                        contents: [
                            importFrom("BuildXL.Tools").NinjaGraphBuilder.exe,
                            importFrom("BuildXL.Tools.Ninjson").pkg.contents 
                        ]
                    }
                ]
            }
        ]
    });
}
