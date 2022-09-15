// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nuget {
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.FrontEnd.Nuget",
        sources: globR(d`.`, "*.cs"),
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Xml.dll,
                NetFx.System.Xml.Linq.dll
            ),
            Script.dll,
            Core.dll,
            importFrom("BuildXL.Core.UnitTests").EngineTestUtilities.dll,
            importFrom("BuildXL.Engine").Engine.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.FrontEnd").Core.dll,
            importFrom("BuildXL.FrontEnd").Nuget.dll,
            importFrom("BuildXL.FrontEnd").Script.dll,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            importFrom("BuildXL.FrontEnd").TypeScript.Net.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("Newtonsoft.Json").pkg,
            ...BuildXLSdk.tplPackages,
        ],
        runtimeContent: [
            {
                subfolder: r`Sdk/Sdk.Managed.Tools.BinarySigner`,
                contents: glob(d`../DscLibs/BinarySigner`, "*.dsc"),
            },
            {
                subfolder: r`Sdk/Sdk.Json`,
                contents: glob(d`../DscLibs/Json`, "*.dsc"),
            }
        ],
        runTestArgs: {
            unsafeTestRunArguments: {
                // These tests require Detours to run itself, so we won't detour the test runner process itself
                runWithUntrackedDependencies: true,
            },
        }
    });
}
