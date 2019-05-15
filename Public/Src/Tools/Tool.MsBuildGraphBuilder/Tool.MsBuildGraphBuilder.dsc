// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as BuildXLSdk from "Sdk.BuildXL";
import {Transformer} from "Sdk.Transformers";
import * as Deployment from "Sdk.Deployment";
import * as MSBuild from "Sdk.Selfhost.MSBuild";

namespace MsBuildGraphBuilder {
    // TODO: We want this to be netstandard too but since Build Prediction is not, we have to keep it net47 only
    export declare const qualifier: MSBuild.MSBuildQualifier;

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
            importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
            importFrom("System.Collections.Immutable").pkg,
            importFrom("DataflowForMSBuildRuntime").pkg,
            importFrom("Microsoft.Build.Prediction").BuildPrediction.dll,
            NetFx.System.Threading.Tasks.dll,
            ...MSBuild.msbuildReferences,
        ],
        runtimeContent: [
            f`App.config`,
        ],
        runtimeContentToSkip: [
            // don't add msbuild dlls because assembly resolvers will resolve msbuild from other MSBuild installations
            ...MSBuild.msbuildReferences,
            importFrom("System.Threading.Tasks.Dataflow").pkg
        ],
        internalsVisibleTo: [
            "Test.Tool.ProjectGraphBuilder",
        ]
    });
}
