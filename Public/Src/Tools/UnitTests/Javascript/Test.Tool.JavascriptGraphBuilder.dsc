// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Node from "Sdk.NodeJs";

namespace Test.Tool.JavascriptGraphBuilder {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    // Yarn is not easily available in the hosted machines that run the public build. So excluding these tests for now outside of the internal build
    const isRunningOnSupportedSystem = 
    Context.getCurrentHost().cpuArchitecture === "x64" && 
    !BuildXLSdk.isHostOsOsx &&
    Environment.getFlag("[Sdk.BuildXL]microsoftInternal");

    @@public
    export const dll = isRunningOnSupportedSystem && BuildXLSdk.test({
        assemblyName: "Test.Tool.JavascriptGraphBuilder",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.FrontEnd").JavaScript.dll,
            importFrom("BuildXL.FrontEnd").Yarn.dll,
            importFrom("BuildXL.FrontEnd").Utilities.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("Newtonsoft.Json").pkg,
        ],
        runtimeContent: [
            {
                subfolder: r`yarn`,
                contents: [importFrom("Yarn").getYarn()],
            },
            {
                subfolder: a`node`,
                contents: [Node.Node.nodeExecutables]
            },
        ],
        testFramework: importFrom("Sdk.Managed.Testing.XUnit").framework,
        runTestArgs:{
            unsafeTestRunArguments: {
                untrackedScopes: [
                    // dotnet may access debugger dlls from a VS install location
                    ...addIfLazy(Context.getCurrentHost().os === "win", () => [
                        d`${Context.getMount("ProgramFilesX86").path}/Microsoft Visual Studio/2019`,
                        d`${Context.getMount("ProgramFiles").path}/Microsoft Visual Studio/2019`,
                    ]),
                ]
            }
        }
    });
}