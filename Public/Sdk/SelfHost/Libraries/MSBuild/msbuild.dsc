// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as BuildXLSdk from "Sdk.BuildXL";

export declare const qualifier: BuildXLSdk.DefaultQualifierWithNet472;

@@public
export const msbuildReferences: Managed.ManagedNugetPackage[] = [
    importFrom("Microsoft.Build.Framework").pkg,
    importFrom("Microsoft.Build.Utilities.Core").pkg,
    importFrom("Microsoft.Build").pkg,
    importFrom("Microsoft.Build.Tasks.Core").pkg,
    importFrom("Microsoft.NET.StringTools").pkg,
    ...addIf(BuildXLSdk.isFullFramework, importFrom("System.Runtime.CompilerServices.Unsafe").withQualifier({targetFramework: "netstandard2.0"}).pkg),
];

/** Runtime content for tests */
@@public
export const msbuildRuntimeContent = [
    importFrom("Microsoft.Build.Runtime").pkg,
    importFrom("System.Memory").pkg,
    importFrom("System.Numerics.Vectors").withQualifier({targetFramework: "netstandard2.0"}).pkg,
    importFrom("System.Collections.Immutable.ForVBCS").pkg,
    importFrom("System.Runtime.CompilerServices.Unsafe").pkg,
    importFrom("System.Threading.Tasks.Dataflow").pkg,
    
    ...BuildXLSdk.isDotNetCoreOrStandard ? [
        importFrom("System.Text.Encoding.CodePages").withQualifier({targetFramework: "netstandard2.0"}).pkg,
        importFrom("Microsoft.Build.Tasks.Core").pkg,
        importFrom("Microsoft.Build.Runtime").Contents.all.getFile(r`contentFiles/any/net6.0/MSBuild.dll`),
        importFrom("Microsoft.Build.Runtime").Contents.all.getFile(r`contentFiles/any/net6.0/MSBuild.runtimeconfig.json`),
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
export const deployment = [
    {
        subfolder: a`msbuild`,
        contents: [{
            subfolder: getFrameworkFolder(),
            contents: [
                ...msbuildRuntimeContent,
                ...msbuildReferences,]
        }]
    },
];