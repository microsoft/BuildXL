// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import { NetFx } from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as Managed from "Sdk.Managed";
import * as MSBuild from "Sdk.Selfhost.MSBuild";

namespace Tests {

    export declare const qualifier : MSBuild.MSBuildQualifier;

    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "BuildPredictionTests",
        sources: globR(d`.`, "*.cs"),
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Xml.dll
            ),
            BuildPrediction.dll,
            ...MSBuild.msbuildReferences,
            ...MSBuild.msbuildLocatorReferences,
        ],
        runtimeContent: [
            ...MSBuild.msbuildRuntimeContent,
            {
                subfolder: "TestsData",
                contents: [
                    Deployment.createFromDisk(d`TestsData`)
                ],
            }
        ],
        runtimeContentToSkip : [
            importFrom("System.Threading.Tasks.Dataflow").pkg
        ]
    });
}
