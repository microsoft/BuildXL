// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as MSBuild from "Sdk.Selfhost.MSBuild";
import * as Frameworks from "Sdk.Managed.Frameworks";
import * as Node from "Sdk.NodeJs";
import {Transformer} from "Sdk.Transformers";

namespace Test.Yarn {
    
    // TODO: to enable this, we should use an older version of NodeJs for Linux
    // Yarn is not easily available in the hosted machines that run the public build. So excluding these tests for now outside of the internal build
    const isRunningOnSupportedSystem = 
        Context.getCurrentHost().cpuArchitecture === "x64" && 
        Environment.getFlag("[Sdk.BuildXL]microsoftInternal");

    @@public
    export const dll = isRunningOnSupportedSystem && BuildXLSdk.test({
        // QTest is not supporting opaque directories as part of the deployment
        testFramework: importFrom("Sdk.Managed.Testing.XUnit").framework,
        runTestArgs: {
            unsafeTestRunArguments: {
                // These tests require Detours to run itself, so we won't detour the test runner process itself
                runWithUntrackedDependencies: true,
            },
        },
        assemblyName: "Test.BuildXL.FrontEnd.Yarn",
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
            importFrom("BuildXL.FrontEnd").Yarn.dll,
            importFrom("BuildXL.FrontEnd").TypeScript.Net.dll,
            importFrom("BuildXL.FrontEnd").Utilities.dll,
            importFrom("BuildXL.FrontEnd").SdkProjectGraph.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
        ],
        runtimeContent: [
            // We need Yarn and Node to run these tests
            {
                subfolder: r`yarn`,
                contents: [importFrom("Yarn").getYarn()],
            },
            {
                subfolder: a`node`,
                contents: [Node.Node.nodeExecutables]
            },
        ],
    });
}
