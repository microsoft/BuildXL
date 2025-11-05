// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as MSBuild from "Sdk.Selfhost.MSBuild";

namespace Test.Tool.MsBuildGraphBuilder {

    export declare const qualifier: BuildXLSdk.Net8QualifierWithNet472;

    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.Tool.ProjectGraphBuilder",
        sources: globR(d`.`, "*.cs"),
        assemblyBindingRedirects: [
            ...importFrom("Sdk.BuildXL").bxlBindingRedirects(),
            // Microsoft.Build.Prediction asks for an older version of System.Text.Json 
            {
                name: "System.Text.Json",
                publicKeyToken: "cc7b13ffcd2ddd51",
                culture: "neutral",
                oldVersion: "0.0.0.0-9.0.0.9",
                newVersion: "9.0.0.9",  // Corresponds to: { id: "System.Text.Json", version: "9.0.9" },
            }
        ],
        references:[
            importFrom("BuildXL.Tools").MsBuildGraphBuilder.exe,
            importFrom("Microsoft.Build.Prediction").pkg,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("BuildXL.FrontEnd").MsBuild.Serialization.dll,
            ...MSBuild.msbuildReferences,
        ],
        runtimeContent: [
            ...MSBuild.msbuildRuntimeContent,
            ...MSBuild.msbuildReferences,
        ],
        runtimeContentToSkip: [
            importFrom("Microsoft.Bcl.AsyncInterfaces").pkg
        ],
    });
}
