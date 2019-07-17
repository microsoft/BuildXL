// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as BuildXLSdk from "Sdk.BuildXL";
import {Transformer} from "Sdk.Transformers";
import * as Deployment from "Sdk.Deployment";
import * as MSBuild from "Sdk.Selfhost.MSBuild";
import * as Frameworks from "Sdk.Managed.Frameworks";
import * as Shared from "Sdk.Managed.Shared";

namespace MsBuildGraphBuilder {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "ProjectGraphBuilder",
        skipDocumentationGeneration: true,
        skipDefaultReferences: true,
        sources: globR(d`.`, "*.cs"),
        references:[
            importFrom("Newtonsoft.Json").pkg,
            importFrom("BuildXL.FrontEnd").MsBuild.Serialization.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
            ...addIf(BuildXLSdk.isFullFramework, importFrom("System.Collections.Immutable").pkg),
            ...addIf(BuildXLSdk.isFullFramework, importFrom("System.Threading.Tasks.Dataflow").pkg),
            importFrom("Microsoft.Build.Prediction").pkg,
            NetFx.System.Threading.Tasks.dll,
            ...MSBuild.msbuildReferences,
        ],
        runtimeContent: [
            f`App.config`,
        ],
        runtimeContentToSkip: [
            // don't add msbuild dlls because assembly resolvers will resolve msbuild from other MSBuild installations
            ...MSBuild.msbuildReferences,
        ],
        internalsVisibleTo: [
            "Test.Tool.ProjectGraphBuilder",
        ]
    });

    @@public
    export const deployment : Deployment.Definition = { contents: [{
        subfolder: r`MsBuildGraphBuilder`,
        contents: [
            {
                subfolder: r`net472`,
                contents: [
                        $.withQualifier(Object.merge<BuildXLSdk.DefaultQualifier>(qualifier, {targetFramework: "net472"}))
                        .MsBuildGraphBuilder.exe
                    ]
            },
            {
                subfolder: r`dotnetcore`,
                contents: [
                        $.withQualifier(Object.merge<BuildXLSdk.DefaultQualifier>(qualifier, {targetFramework: "netcoreapp3.0"}))
                        .MsBuildGraphBuilder.exe
                    ]
            }
        ]
    }]};
}
