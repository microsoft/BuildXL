// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";
import * as MemoizationStore from "BuildXL.Cache.MemoizationStore";
import * as Managed from "Sdk.Managed";
import * as Nuget from "Sdk.Managed.Tools.NuGet";

export declare const qualifier : BuildXLSdk.AllSupportedQualifiers;

export {BuildXLSdk};

export const NetFx = BuildXLSdk.NetFx;

// Need to exclude netstandard.dll reference when calling this function for creating a nuget package.
@@public
export function getSerializationPackages(includeNetStandard: boolean) : (Managed.ManagedNugetPackage | Managed.Assembly)[] {
    return [
        importFrom("System.Memory.Data").pkg,
        ...getSystemTextJson(includeNetStandard),
        ...getSerializationPackagesWithoutNetStandard()
    ];
}

@@public
export function getSerializationPackagesWithoutNetStandard() : (Managed.ManagedNugetPackage)[] {
    return [
        ...(BuildXLSdk.isFullFramework ? [
            importFrom("System.Runtime.CompilerServices.Unsafe").pkg,
        ] : []),
        ...(BuildXLSdk.isFullFramework || qualifier.targetFramework === "netstandard2.0" ? [
            importFrom("System.Memory").pkg,
        ] : []),
        importFrom("System.Text.Encodings.Web").pkg,
        importFrom("System.Numerics.Vectors").pkg,
    ];
}

@@public
export function getSystemTextJson(includeNetStandard: boolean) : (Managed.ManagedNugetPackage | Managed.Assembly)[] {
    return [
        ...(includeNetStandard && BuildXLSdk.isFullFramework ? [
            BuildXLSdk.withQualifier({targetFramework: "net472"}).NetFx.Netstandard.dll,
        ] : [
        ]),
        ...getSystemTextJsonWithoutNetStandard(),
    ];
}

@@public
export function getSystemTextJsonWithoutNetStandard() : Managed.ManagedNugetPackage[] {
    return [
        ...addIf(
            !BuildXLSdk.isDotNetCore,
            importFrom("System.Text.Json").pkg),
    ];
}

@@public
export function getProtobufPackages() : Managed.ManagedNugetPackage[] {
    return [
        importFrom("System.Buffers").pkg,
        importFrom("System.Memory").pkg,
        importFrom("Google.Protobuf").pkg, 
    ];
}

@@public
export function getGrpcPackages(includeNetStandard: boolean) : (Managed.ManagedNugetPackage | Managed.Assembly)[] {
    return [
        ...(BuildXLSdk.isFullFramework && includeNetStandard ? [
                NetFx.System.IO.dll,
                NetFx.Netstandard.dll
            ] : []
        ),
        ...getGrpcPackagesWithoutNetStandard()
    ];
}

@@public
export function getGrpcPackagesWithoutNetStandard() : Managed.ManagedNugetPackage[] {
    return [
        ...getProtobufPackages(),
        importFrom("Grpc.Core").pkg,
        importFrom("Grpc.Core.Api").pkg,
        ...BuildXLSdk.bclAsyncPackages,
    ];
}

@@public
export function getGrpcDotNetPackages() : Managed.ManagedNugetPackage[] {
    return [
        ...addIfLazy(BuildXLSdk.isDotNetCoreOrStandard, () => [
                  importFrom("Grpc.Net.Common").pkg,
                  importFrom("Grpc.Net.Client").pkg,
                  // Grpc.Net.Common depends on Grpc.Core.Api, but this package should
                  // already be included as pat of 'getGrpcPackages'.
                  // Once the migration from Grpc.Core is done, this method should be updated
                  // to include 'Grpc.Core.Api' package as well.
        ])
    ];
}

@@public
export function getGrpcAspNetCorePackages() : (Managed.ManagedNugetPackage | Managed.Assembly)[] {
    return [
        ...getGrpcDotNetPackages(),
        ...addIfLazy(BuildXLSdk.isDotNetCore, () => [
                  importFrom("Grpc.Net.Client.Web").pkg,
                  importFrom("Grpc.Net.ClientFactory").pkg,
                 
                  importFrom("Grpc.AspNetCore.Server.ClientFactory").pkg,
                  importFrom("Grpc.AspNetCore.Server").pkg,
                  
                  importFrom("System.Security.Cryptography.ProtectedData").pkg,
                  
                  ...getAsptNetCoreAssemblies(),
        ])
    ];
}

@@public
export function getAsptNetCoreAssemblies() : Managed.ManagedNugetPackage[] {
    return [
        // The ASPNet web framework package comes with some DLLs that we also import individually.
        // We prefer the one individually imported, so remove the overlapping DLLs from the web framework
        // package and add the individually imported ones.
        // If you ever get a double deployment issue coming from this package, just add the conflicting DLL
        // under getWebFrameworkExclusions()
        Managed.Factory.filterRuntimeSpecificBinaries(
                BuildXLSdk.WebFramework.getFrameworkPackage(), 
                getWebFrameworkExclusions()),
            ...getWebFrameworkExclusions()
    ];
}

function getWebFrameworkExclusions(): Managed.ManagedNugetPackage[] {
    return [
        importFrom("System.IO.Pipelines").pkg,
        importFrom("System.Diagnostics.EventLog").pkg,
        importFrom("System.Security.Cryptography.Pkcs").pkg,
        importFrom("System.Security.Cryptography.Xml").pkg,
        importFrom("Microsoft.Extensions.DependencyInjection").pkg,
        importFrom("Microsoft.Extensions.DependencyInjection.Abstractions").pkg,
        importFrom("Microsoft.Extensions.Http").pkg,
        importFrom("Microsoft.Extensions.Logging").pkg,
        importFrom("Microsoft.Extensions.Options").pkg,
        importFrom("Microsoft.Extensions.Primitives").pkg
    ];
}

@@public
export function getProtobufNetPackages(includeNetStandard: boolean) : (Managed.ManagedNugetPackage | Managed.Assembly)[] {
    return [
        ...getGrpcPackages(includeNetStandard),
        importFrom("protobuf-net.Core").pkg,
        importFrom("protobuf-net").pkg,
        importFrom("protobuf-net.Grpc").pkg,
        importFrom("protobuf-net.Grpc.Native").pkg,
    ];
}

@@public
export function getAzureBlobStorageSdkPackages(includeNetStandard: boolean) : (Managed.ManagedNugetPackage | Managed.Assembly)[] {
    return [
        ...getAzureBlobStorageSdkPackagesWithoutNetStandard(),
        ...getSerializationPackages(includeNetStandard),
        ...BuildXLSdk.getSystemMemoryPackages(includeNetStandard),
    ];
}

@@public
export function getAzureBlobStorageSdkPackagesWithoutNetStandard() : (Managed.ManagedNugetPackage)[] {
    return [
        importFrom("Azure.Storage.Blobs").pkg,
        importFrom("Azure.Storage.Common").pkg,
        importFrom("Azure.Core").pkg,
        importFrom("Azure.Storage.Blobs.Batch").pkg,
        importFrom("Azure.Storage.Blobs.ChangeFeed").pkg,
        importFrom("Azure.Identity").pkg,
    ];
}

namespace Default {
    export declare const qualifier: BuildXLSdk.DefaultQualifierWithNet472;

    @@public
    export const deployment: Deployment.Definition =
    {
        contents: [
            {
                subfolder: r`App`,
                contents: [
                    App.exe
                ]
            },
            {
                subfolder: r`Distributed`,
                contents: [
                    Distributed.dll
                ]
            },
            {
                subfolder: r`Grpc`,
                contents: [
                    Grpc.dll
                ]
            },
            {
                subfolder: r`Interfaces`,
                contents: [
                    Interfaces.dll
                ]
            },
            {
                subfolder: r`Library`,
                contents: [
                    Library.dll
                ]
            },
            ...addIf(BuildXLSdk.Flags.isVstsArtifactsEnabled,
                {
                    subfolder: r`Vsts`,
                    contents: [
                        Vsts.dll
                    ]
                },
                {
                    subfolder: r`VstsInterfaces`,
                    contents: [
                        VstsInterfaces.dll
                    ]
                }
            ),
            {
                subfolder: r`Host`,
                contents: [
                    importFrom("BuildXL.Cache.DistributedCache.Host").Service.dll,
                ]
            },
            {
                subfolder: r`Hashing`,
                contents: [
                    Hashing.dll,
                ]
            },
            {
                subfolder: r`UtilitiesCore`,
                contents: [
                    UtilitiesCore.dll,
                ]
            },
        ]
    };
}

// TODO: Merge into Default.deployment when all of CloudStore builds on DotNetCore
namespace DotNetCore {
    export declare const qualifier: BuildXLSdk.NetCoreAppQualifier;

    @@public
    export const deployment: Deployment.NestedDefinition =
    {
        subfolder: r`ContentStore`,
        contents: [
            {
                subfolder: r`Interfaces`,
                contents: [
                    Interfaces.dll
                ]
            },
        ]
    };
}

/**
 * This is an inception old deployment used by BuildXL.
 */
@@public
export const deploymentForBuildXL: Deployment.Definition = {
    contents: [
        App.exe,

        ...getGrpcPackages(true),

        ...addIf(qualifier.targetRuntime === "win-x64",
            importFrom("Grpc.Core").Contents.all.getFile("runtimes/win-x64/native/grpc_csharp_ext.x64.dll")),
        ...addIf(qualifier.targetRuntime === "osx-x64",
            importFrom("Grpc.Core").Contents.all.getFile("runtimes/osx-x64/native/libgrpc_csharp_ext.x64.dylib")),
    ]
};
