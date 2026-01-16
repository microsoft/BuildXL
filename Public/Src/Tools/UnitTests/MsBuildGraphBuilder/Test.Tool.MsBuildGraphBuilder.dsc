// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as MSBuild from "Sdk.Selfhost.MSBuild";

namespace Test.Tool.MsBuildGraphBuilder {

    export declare const qualifier: BuildXLSdk.Net9QualifierWithNet472;

    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.Tool.ProjectGraphBuilder",
        sources: globR(d`.`, "*.cs"),
        assemblyBindingRedirects: [
            ...importFrom("Sdk.BuildXL").bxlBindingRedirects(),
            // Microsoft.Build.Prediction asks for an older version of some assemblies 
            {
                name: "System.Text.Json",
                publicKeyToken: "cc7b13ffcd2ddd51",
                culture: "neutral",
                oldVersion: "0.0.0.0-9.0.0.9",
                newVersion: "9.0.0.10",  // Corresponds to: { id: "System.Text.Json", version: "9.0.10" },
            },
            {
                name: "System.Threading.Tasks.Extensions",
                publicKeyToken: "cc7b13ffcd2ddd51",
                culture: "neutral",
                oldVersion: "0.0.0.0-4.99.99.99",
                newVersion: "4.2.1.0", // Corresponds to: { id: "System.Threading.Tasks.Extensions" },
            },
            {
                name: "Microsoft.IO.Redist",
                publicKeyToken: "cc7b13ffcd2ddd51",
                culture: "neutral",
                oldVersion: "0.0.0.0-6.99.99.99",
                newVersion: "6.1.0.0", // Corresponds to: { id: "Microsoft.IO.Redist" },
            },
            {
                name: "System.Memory",
                publicKeyToken: "cc7b13ffcd2ddd51",
                culture: "neutral",
                oldVersion: "0.0.0.0-4.0.2.0",
                newVersion: "4.0.5.0", // Corresponds to: { id: "System.Memory", version: "4.6.3" },
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
    });
}
