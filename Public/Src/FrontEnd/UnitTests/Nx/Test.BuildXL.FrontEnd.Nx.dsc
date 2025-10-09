// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as MSBuild from "Sdk.Selfhost.MSBuild";
import * as Frameworks from "Sdk.Managed.Frameworks";
import {Node} from "Sdk.NodeJs";
import {Transformer} from "Sdk.Transformers";

namespace Test.Nx {
    
    // Running for internal only
    const isRunningOnSupportedSystem = 
        Context.getCurrentHost().cpuArchitecture === "x64" && 
        Environment.getFlag("[Sdk.BuildXL]microsoftInternal");

    // Install Nx for tests
    const nxTest = Context.getNewOutputDirectory(a`nx-test`);
    const nx = Node.runNpmPackageInstall(nxTest, [], {name: "nx", version: "20.8.2"}, false);

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
        assemblyName: "Test.BuildXL.FrontEnd.Nx",
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
            importFrom("BuildXL.FrontEnd").Nx.dll,
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
            // We need Node, Yarn and Nx to run these tests
            {
                subfolder: a`node`,
                contents: [Node.nodePackage]
            },
            {
                subfolder: a`nx-deployment`,
                contents: [nx]
            },
            {
                subfolder: r`yarn`,
                contents: [importFrom("Yarn").getYarn()],
            }
        ],
    });
}
