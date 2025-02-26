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
                oldVersion: "0.0.0.0-9.0.0.2",
                newVersion: "9.0.0.2",  // Corresponds to: { id: "System.Text.Json", version: "9.0.2" },
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
            // Remove some of the dependencies to avoid a clash between two different versions being deployed.
            ...MSBuild.msbuildRuntimeContent.filter(
                dep => typeof dep === "File" || 
                    (dep.name !== "System.Numerics.Vectors" && 
                    dep.name !== "System.Runtime.CompilerServices.Unsafe" && 
                    dep.name !== "System.Collections.Immutable.dll")),
            ...MSBuild.msbuildReferences,
        ],
        runtimeContentToSkip: [
            importFrom("Microsoft.Bcl.AsyncInterfaces").pkg
        ],
    });
}
