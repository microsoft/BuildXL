// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

@@public
export const systemThreadingChannelsPackages : Managed.ManagedNugetPackage[] = 
    // System.Threading.Channels comes bundled with .NET Core, so we don't need to provide it. If we do,
    // the version we provide will likely conflict with the official one
    isFullFramework || !isDotNetCoreApp
        // Needed because net472 -> netstandard2.0 translation is not yet supported by the NuGet resolver.    
        ? [importFrom("System.Threading.Channels").withQualifier({ targetFramework: "netstandard2.0" }).pkg]
        : [];

@@public
export const asyncInterfacesPackage : Managed.ManagedNugetPackage = 
    // .NET Core version is tricky, because there are some crucial differences between .netcoreapp and netstandard
    (isDotNetCoreApp 
    ? importFrom("Microsoft.Bcl.AsyncInterfaces").withQualifier({targetFramework: "netstandard2.1"}).pkg
    : importFrom("Microsoft.Bcl.AsyncInterfaces").pkg);

@@public
export const bclAsyncPackages : Managed.ManagedNugetPackage[] = [
        importFrom("System.Threading.Tasks.Extensions").pkg,
        importFrom("System.Linq.Async").pkg,
        asyncInterfacesPackage
    ];

@@public
export const systemThreadingTasksDataflowPackageReference : Managed.ManagedNugetPackage[] = 
    isDotNetCoreApp ? [] : [
            importFrom("System.Threading.Tasks.Dataflow").pkg,
        ];

@@public 
export const systemMemoryDeployment = getSystemMemoryPackages(true);

// This is meant to be used only when declaring NuGet packages' dependencies. In that particular case, you should be
// calling this function with includeNetStandard: false
@@public 
export function getSystemMemoryPackages(includeNetStandard: boolean) : Managed.ManagedNugetPackage[] {
    return [
        ...(isDotNetCoreApp ? [] : [
            importFrom("System.Memory").withQualifier({targetFramework: "netstandard2.0"}).pkg,
            importFrom("System.Buffers").withQualifier({targetFramework: "netstandard2.0"}).pkg,
            importFrom("System.Runtime.CompilerServices.Unsafe").withQualifier({targetFramework: "netstandard2.0"}).pkg,
            importFrom("System.Numerics.Vectors").withQualifier({targetFramework: "netstandard2.0"}).pkg,
            ...(includeNetStandard ? [
                // It works to reference .NET472 all the time because netstandard.dll targets .NET4 so it's safe for .NET462 to do so.
                $.withQualifier({targetFramework: "net472"}).NetFx.Netstandard.dll,
            ]
            : []),
        ]
        ),
    ];
}
