// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as MSBuild from "Sdk.Selfhost.MSBuild";
import * as Frameworks from "Sdk.Managed.Frameworks";
import {Node} from "Sdk.NodeJs";
import {Transformer} from "Sdk.Transformers";

namespace Test.Lage {
    
    // Running for internal only
    const isRunningOnSupportedSystem = 
        Context.getCurrentHost().cpuArchitecture === "x64" && 
        Environment.getFlag("[Sdk.BuildXL]microsoftInternal");

    // Install Lage for tests
    const lageTest = Context.getNewOutputDirectory(a`lage-test`);
    const lage = Node.runNpmPackageInstall(lageTest, [], {name: "lage", version: "1.9.2"}, false);

    @@public
    export const dll = isRunningOnSupportedSystem && BuildXLSdk.test({
        // QTest is not supporting opaque directories as part of the deployment
        testFramework: importFrom("Sdk.Managed.Testing.XUnit").framework,
        runTestArgs: {
            // Under linux (EBPF) these tests probe files all over the place (yarn most likely to blame)
            // TODO: we should try to keep accesses under control, otherwise these tests will almost never be a cache hit
            allowUndeclaredSourceReads: BuildXLSdk.Flags.IsEBPFSandboxForTestsEnabled,
            unsafeTestRunArguments: {
                // These tests require Detours to run itself, so we won't detour the test runner process itself
                runWithUntrackedDependencies: !BuildXLSdk.Flags.IsEBPFSandboxForTestsEnabled,
                untrackedScopes: [
                    // The V8 compiler accesses the temp directory
                    // Npm dumps logs under $HOME/.npm
                    ...addIfLazy(!Context.isWindowsOS(), () => [d`/tmp`, d`${Environment.getDirectoryValue("HOME")}/.npm`])
                ]
            },
        },
        assemblyName: "Test.BuildXL.FrontEnd.Lage",
        sources: globR(d`.`, "*.cs"), 
        references: [
            Script.dll,
            Core.dll,
            importFrom("BuildXL.Core.UnitTests").EngineTestUtilities.dll,
            importFrom("BuildXL.Engine").Engine.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.FrontEnd").Core.dll,
            importFrom("BuildXL.FrontEnd").Script.dll,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            importFrom("BuildXL.FrontEnd").JavaScript.dll,
            importFrom("BuildXL.FrontEnd").Lage.dll,
            importFrom("BuildXL.FrontEnd").TypeScript.Net.dll,
            importFrom("BuildXL.FrontEnd").Utilities.dll,
            importFrom("BuildXL.FrontEnd").SdkProjectGraph.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
        ],
        runtimeContent: [
            // We need Node, Yarn and Lage to run these tests
            {
                subfolder: a`node`,
                contents: [Node.nodePackage]
            },
            {
                subfolder: a`lage`,
                contents: [lage]
            },
            {
                subfolder: r`yarn`,
                contents: [importFrom("Yarn").getYarn()],
            },
            {
                subfolder: r`lage-mock`,
                contents: [Mock.exe],
            }
        ],
    });
}
