// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import { NetFx } from "Sdk.BuildXL";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as Managed from "Sdk.Managed";
import * as MSBuild from "Sdk.Selfhost.MSBuild";

namespace BuildPrediction {
    export declare const qualifier : MSBuild.MSBuildQualifier;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "Microsoft.Build.Prediction",
        generateLogs: false,
        sources: globR(d`.`, "*.cs"),
        references: [
            ...MSBuild.msbuildReferences,
            ...MSBuild.msbuildLocatorReferences,
        ],
        internalsVisibleTo: [
            "BuildPredictionTests",
        ],
    });
}