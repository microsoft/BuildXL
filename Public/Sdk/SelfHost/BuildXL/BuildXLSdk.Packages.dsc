// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

@@public
export const systemThreadingChannelsPackages : Managed.ManagedNugetPackage[] = 
    // System.Threading.Channels comes bundled with .NET Core, so we don't need to provide it. If we do,
    // the version we provide will likely conflict with the official one
    isFullFramework || !isDotNetCore
        // Needed because net472 -> netstandard2.0 translation is not yet supported by the NuGet resolver.    
        ? [importFrom("System.Threading.Channels").withQualifier({ targetFramework: "netstandard2.0" }).pkg]
        : [];

@@public
export const bclAsyncPackages : Managed.ManagedNugetPackage[] = [
        importFrom("System.Threading.Tasks.Extensions").pkg,
        importFrom("System.Linq.Async").pkg,
        importFrom("Microsoft.Bcl.AsyncInterfaces").pkg,
    ];

@@public
export const systemThreadingTasksDataflowPackageReference : Managed.ManagedNugetPackage[] = 
    isDotNetCore ? [] : [
            importFrom("System.Threading.Tasks.Dataflow").pkg,
        ];

@@public 
export const systemMemoryDeployment = getSystemMemoryPackages(true);

// This is meant to be used only when declaring NuGet packages' dependencies. In that particular case, you should be
// calling this function with includeNetStandard: false
@@public 
export function getSystemMemoryPackages(includeNetStandard: boolean) : (Managed.ManagedNugetPackage | Managed.Assembly)[] {
    return [
        ...(!isDotNetCore && includeNetStandard ? [
            $.withQualifier({targetFramework: "net472"}).NetFx.Netstandard.dll,
        ] : []),
        ...getSystemMemoryPackagesWithoutNetStandard()
    ];
}

@@public 
export function getSystemMemoryPackagesWithoutNetStandard() : Managed.ManagedNugetPackage[] {
    return [
        ...(isDotNetCore ? [] : [
            importFrom("System.Memory").pkg,
            importFrom("System.Buffers").pkg,
            importFrom("System.Runtime.CompilerServices.Unsafe").pkg,
            importFrom("System.Numerics.Vectors").pkg,
        ]
        ),
    ];
}