// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as MSBuild from "Sdk.Selfhost.MSBuild";

namespace Test.Tool.MsBuildGraphBuilder {

    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.Tool.ProjectGraphBuilder",
        sources: globR(d`.`, "*.cs"),
        appConfig: f`app.config`,
        testFramework: importFrom("Sdk.Managed.Testing.XUnit").framework,
        references:[
            importFrom("BuildXL.Tools").MsBuildGraphBuilder.exe,
            importFrom("Microsoft.Build.Prediction").pkg,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("BuildXL.FrontEnd").MsBuild.Serialization.dll,
            ...MSBuild.msbuildReferences,
            BuildXLSdk.Factory.createAssembly(importFrom("System.Memory").Contents.all, r`lib/netstandard2.0/System.memory.dll`),
        ],
        runtimeContent: [
            ...MSBuild.msbuildRuntimeContent,
            ...MSBuild.msbuildReferences,
        ],
        runtimeContentToSkip: [
            importFrom("System.Threading.Tasks.Dataflow").pkg
        ]
    });
}
