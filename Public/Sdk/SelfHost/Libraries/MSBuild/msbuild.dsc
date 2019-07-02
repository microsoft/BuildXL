// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as BuildXLSdk from "Sdk.BuildXL";
// import * as Deployment from "Sdk.Deployment";

@@public
export interface MSBuildQualifier extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "net472" ;
    targetRuntime: "win-x64" | "osx-x64";
};

export declare const qualifier : MSBuildQualifier;

@@public
export const msbuildReferences: Managed.ManagedNugetPackage[] = [
    importFrom("Microsoft.Build.Framework").pkg,
    importFrom("Microsoft.Build.Utilities.Core").pkg,
    importFrom("Microsoft.Build").pkg,
    importFrom("Microsoft.Build.Tasks.Core").pkg,
];

@@public
export const msbuildRuntimeContent = [
    importFrom("System.Numerics.Vectors").pkg,
    importFrom("DataflowForMSBuildRuntime").pkg,
    // importFrom("System.Collections.Immutable").pkg,
    ...BuildXLSdk.isTargetRuntimeOsx ? [
        importFrom("Microsoft.Build.Runtime").Contents.all.getFile(r`contentFiles/any/netcoreapp2.1/MSBuild.dll`),
        importFrom("Microsoft.Build.Runtime").Contents.all.getFile(r`contentFiles/any/netcoreapp2.1/MSBuild.runtimeconfig.json`),
    ]
    : [
        importFrom("Microsoft.Build.Runtime").Contents.all.getFile(r`contentFiles/any/net472/MSBuild.exe`),
        importFrom("Microsoft.Build.Runtime").Contents.all.getFile(r`contentFiles/any/net472/MSBuild.exe.config`),
    ],
];
