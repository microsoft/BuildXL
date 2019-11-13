// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as BuildXLSdk from "Sdk.BuildXL";

export declare const qualifier: BuildXLSdk.DefaultQualifier;

@@public
export const msbuildReferences: Managed.ManagedNugetPackage[] = [
    importFrom("Microsoft.Build.Framework").pkg,
    importFrom("Microsoft.Build.Utilities.Core").pkg,
    importFrom("Microsoft.Build").pkg,
    importFrom("Microsoft.Build.Tasks.Core").pkg,
];

/** Runtime content for tests */
@@public
export const msbuildRuntimeContent = [
    importFrom("Microsoft.Build.Runtime").pkg,
    importFrom("SystemMemoryForMSBuild").withQualifier({targetFramework: "netstandard2.0"}).pkg,
    importFrom("SystemNumericsVectorsForMSBuild").pkg,
    importFrom("SystemRuntimeCompilerServicesUnsafeForMSBuild").withQualifier({targetFramework: "netstandard2.0"}).pkg,
    importFrom("System.Threading.Tasks.Dataflow").pkg,
    
    ...BuildXLSdk.isDotNetCoreBuild ? [
        importFrom("System.Text.Encoding.CodePages").withQualifier({targetFramework: "netstandard2.0"}).pkg,
        importFrom("Microsoft.Build.Tasks.Core").withQualifier({targetFramework: "netstandard2.0"}).pkg,
        importFrom("Microsoft.Build.Runtime").Contents.all.getFile(r`contentFiles/any/netcoreapp2.1/MSBuild.dll`),
        importFrom("Microsoft.Build.Runtime").Contents.all.getFile(r`contentFiles/any/netcoreapp2.1/MSBuild.runtimeconfig.json`),
    ]
    : [
        importFrom("Microsoft.Build.Runtime").Contents.all.getFile(r`contentFiles/any/net472/MSBuild.exe`),
        importFrom("Microsoft.Build.Runtime").Contents.all.getFile(r`contentFiles/any/net472/MSBuild.exe.config`),
    ],
];

function getFrameworkFolder() { 
    return BuildXLSdk.isDotNetCoreBuild ? "dotnetcore" : qualifier.targetFramework;
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