// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as MSBuild from "Sdk.Selfhost.MSBuild";
import * as Frameworks from "Sdk.Managed.Frameworks";
import * as Node from "Sdk.NodeJs";
import {Transformer} from "Sdk.Transformers";

namespace Test.Rush {
    
    // Install Rush for tests
    const rush = Node.Npm.install({
        name: "@microsoft/rush", 
        version: "5.22.0", 
        destinationFolder: Context.getNewOutputDirectory(a`rush-test`)});
    
    const rushlib = Node.Npm.install({
        name: "@microsoft/rush-lib", 
        version: "5.22.0", 
        destinationFolder: Context.getNewOutputDirectory(a`rushlib-test`)});

    @@public
    export const dll = BuildXLSdk.test({
        // QTest is not supporting opaque directories as part of the deployment
        testFramework: importFrom("Sdk.Managed.Testing.XUnit").framework,
        runTestArgs: {
            unsafeTestRunArguments: {
                // These tests require Detours to run itself, so we won't detour the test runner process itself
                runWithUntrackedDependencies: true,
            },
        },
        assemblyName: "Test.BuildXL.FrontEnd.Rush",
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
            importFrom("BuildXL.FrontEnd").Rush.dll,
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
        ],
        runtimeContent: [
            // We need Rush and Node to run these tests
            {
                // We don't really have a proper rush installation, since we are preventing npm
                // to create symlinks by default. So 'simulate' one by placing the expected
                // rush-lib dependency in a nested location.
                subfolder: r`rush/node_modules`,
                contents: [
                    rush.nodeModules,
                    {
                        subfolder: r`@microsoft/rush/node_modules`,
                        contents: [rushlib.nodeModules]
                    }
                ]
            },
            {
                subfolder: a`node`,
                contents: [getNodeExeForRushDirectory()]
            },
        ],
    });

    const nodeWinDir = "node-v12.16.1-win-x64";
    const nodeOsxDir = "node-v12.16.1-darwin-x64";

    function getNodeExeForRushDirectory(): StaticDirectory {
        const host = Context.getCurrentHost();
    
        Contract.assert(host.cpuArchitecture === "x64", "Only 64bit verisons supported.");
    
        let pkgContents : StaticDirectory = undefined;
        
        switch (host.os) {
            case "win":
                pkgContents = Transformer.reSealPartialDirectory(importFrom("NodeJs.ForRush.win-x64").extracted, r`${nodeWinDir}`);
                break;
            case "macOS": 
                pkgContents = Transformer.reSealPartialDirectory(importFrom("NodeJs.ForRush.osx-x64").extracted, r`${nodeOsxDir}\bin`);
                break;
            default:
                Contract.fail(`The current NodeJs package doesn't support the current OS: ${host.os}. Esure you run on a supported OS -or- update the NodeJs package to have the version embdded.`);
        }
        
        return pkgContents;
    }
}
