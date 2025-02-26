// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";

export declare const qualifier: BuildXLSdk.Net8QualifierWithNet472;

@@public
export const msbuildReferences: Managed.ManagedNugetPackage[] = [
    importFrom("Microsoft.Build.Framework").pkg,
    importFrom("Microsoft.Build.Utilities.Core").pkg,
    importFrom("Microsoft.Build").pkg,
    importFrom("Microsoft.Build.Tasks.Core").pkg,
    importFrom("Microsoft.NET.StringTools").pkg,
    ...addIf(BuildXLSdk.isFullFramework, importFrom("System.Runtime.CompilerServices.Unsafe").pkg),
];

/** 
 * Runtime content for tests 
 * Observe that we use a net8-specific version of msbuild.
 **/
@@public
export const msbuildRuntimeContent = [
    importFrom("System.Memory").pkg,
    importFrom("System.Numerics.Vectors").pkg,
    importFrom("System.Collections.Immutable").pkg,
    importFrom("System.Runtime.CompilerServices.Unsafe").pkg,
    importFrom("System.Threading.Tasks.Dataflow").pkg,
    importFrom("System.Threading.Tasks.Extensions").pkg,
    importFrom("Microsoft.Bcl.AsyncInterfaces.v8").pkg,

    ...BuildXLSdk.isDotNetCoreOrStandard ? [
        importFrom("System.Text.Encoding.CodePages").pkg,
        importFrom("Microsoft.Build.Tasks.Core").pkg,
        importFrom("Microsoft.Build.Runtime").Contents.all.getFile(r`contentFiles/any/net8.0/MSBuild.dll`),
        importFrom("Microsoft.Build.Runtime").Contents.all.getFile(r`contentFiles/any/net8.0/MSBuild.runtimeconfig.json`),
        importFrom("Microsoft.NET.StringTools").pkg
    ]
    : [
        importFrom("Microsoft.Build.Runtime").Contents.all.getFile(r`contentFiles/any/net472/MSBuild.exe`),
        importFrom("Microsoft.Build.Runtime").Contents.all.getFile(r`contentFiles/any/net472/MSBuild.exe.config`),
    ],
];

function getFrameworkFolder() : string { 
    return BuildXLSdk.isDotNetCoreOrStandard ? "dotnetcore" : qualifier.targetFramework;
}  

@@public
export const deployment : Deployment.NestedDefinition[] = [
    {
        subfolder: a`msbuild`,
        contents: [
            {
                subfolder: getFrameworkFolder(),
                contents: [
                    ...msbuildRuntimeContent,
                    ...msbuildReferences,
                ],
                // The filter must be defined at the same level as content that it applies to.
                contentToSkip: [
                    // MsBuild.exe needs "Microsoft.Bcl.AsyncInterfaces.v8". 
                    // We skip the newer version of this package to resolve a deployment collision.
                    importFrom("Microsoft.Bcl.AsyncInterfaces").pkg,
                ]
            }
        ]
    },
];