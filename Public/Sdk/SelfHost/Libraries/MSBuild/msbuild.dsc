// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";

export declare const qualifier: BuildXLSdk.Net9QualifierWithNet472;

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
 * Observe that we use a net9-specific version of msbuild.
 **/
@@public
export const msbuildRuntimeContent = [
    importFrom("System.Memory").pkg,    
    importFrom("System.Numerics.Vectors").pkg,
    importFrom("System.Collections.Immutable").pkg,
    importFrom("System.Runtime.CompilerServices.Unsafe").pkg,
    importFrom("System.Threading.Tasks.Dataflow").pkg,
    importFrom("System.Threading.Tasks.Extensions").pkg,
    importFrom("Microsoft.Bcl.AsyncInterfaces").pkg,

    ...BuildXLSdk.isDotNetCoreOrStandard ? [
        importFrom("System.Text.Encoding.CodePages").pkg,
        importFrom("Microsoft.Build.Tasks.Core").pkg,
        importFrom("Microsoft.Build.Runtime").Contents.all.getFile(r`contentFiles/any/net9.0/MSBuild.dll`),
        importFrom("Microsoft.Build.Runtime").Contents.all.getFile(r`contentFiles/any/net9.0/MSBuild.runtimeconfig.json`),
        importFrom("Microsoft.NET.StringTools").pkg
    ]
    : [
        importFrom("Microsoft.Build.Runtime").Contents.all.getFile(r`contentFiles/any/net472/MSBuild.exe`),
        // We have a custom config for msbuild in full framework, which is a copy of the original one that comes
        // from the nuget package, with some binding redirects added.
        f`${Context.getMount("SourceRoot").path}/Public/Src/FrontEnd/UnitTests/MsBuild/msbuild.exe.config`    ],
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
            }
        ]
    },
];