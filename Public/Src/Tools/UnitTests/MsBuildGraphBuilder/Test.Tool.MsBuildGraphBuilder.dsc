// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as MSBuild from "Sdk.Selfhost.MSBuild";

namespace Test.Tool.MsBuildGraphBuilder {

    export declare const qualifier: MSBuild.MSBuildQualifier;

    // If the current qualifier is full framework, this tool has to be built with 472
    const msBuildGraphBuilderReference : Managed.Assembly =
        importFrom("BuildXL.Tools").MsBuildGraphBuilder.withQualifier(to472(qualifier)).exe;

    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.Tool.ProjectGraphBuilder",
        sources: globR(d`.`, "*.cs"),
        // TODO: QTest
        testFramework: importFrom("Sdk.Managed.Testing.XUnit").framework,
        references:[
            msBuildGraphBuilderReference,
            importFrom("Microsoft.Build.Prediction").pkg,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("BuildXL.FrontEnd").MsBuild.Serialization.dll,
            ...MSBuild.msbuildReferences,
        ],
        runtimeContent: [
            ...MSBuild.msbuildRuntimeContent,
            ...MSBuild.msbuildReferences,
        ],
        runtimeContentToSkip : [
            importFrom("System.Threading.Tasks.Dataflow").pkg
        ]
    });

    function to472(aQualifier: (typeof qualifier)) : (typeof qualifier) & {targetFramework: "net472"} {
        return Object.merge<(typeof qualifier) & {targetFramework: "net472"}>(aQualifier, {targetFramework: "net472"});
    }
}
