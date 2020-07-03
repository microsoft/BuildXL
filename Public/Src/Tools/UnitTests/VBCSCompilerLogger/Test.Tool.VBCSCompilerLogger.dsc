// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as MSBuild from "Sdk.Selfhost.MSBuild";

namespace Test.Tool.VBCSCompilerLogger {

    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.Tool.VBCSCompilerLogger",
        sources: globR(d`.`, "*.cs"),
        references:[
            importFrom("BuildXL.Core.UnitTests").EngineTestUtilities.dll,
        ],
        runtimeContent: [
            {
                subfolder: a`Logger`,
                contents: [
                    importFrom("BuildXL.Tools").VBCSCompilerLogger.withQualifier({targetFramework: "net472"}).loggerWithOldCodeAnalysis,
                ]
            },
            ...importFrom("Sdk.Selfhost.MSBuild").withQualifier({targetFramework: "net472"}).deployment,
            {
                subfolder: a`Compilers`,
                contents: [
                    {
                        subfolder: a`net472`,
                        contents: [importFrom("Microsoft.Net.Compilers").Contents.all]
                    },
                ]
            }
        ],
    });
}
