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

@@public
export const kustoPackages = [
    ...(BuildXLSdk.isDotNetCoreBuild ? [
        importFrom("Microsoft.Azure.Kusto.Data.NETStandard").pkg,
        importFrom("Microsoft.Azure.Kusto.Ingest.NETStandard").pkg,
        importFrom("Microsoft.Azure.Kusto.Cloud.Platform.Azure.NETStandard").pkg,
        importFrom("Microsoft.Azure.Kusto.Cloud.Platform.NETStandard").pkg,
        importFrom("Microsoft.Extensions.PlatformAbstractions").pkg,
        importFrom("Microsoft.IO.RecyclableMemoryStream").pkg,
    ] : [
        importFrom("Microsoft.Azure.Kusto.Ingest").pkg,
    ]),
    importFrom("Microsoft.Azure.Management.Kusto").pkg,
    importFrom("Microsoft.IdentityModel.Clients.ActiveDirectory").pkg,
    ...getAzureBlobStorageSdkPackages(true),
];

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
            importFrom("System.Runtime.CompilerServices.Unsafe").withQualifier({ targetFramework: "netstandard2.0" }).pkg,
        ] : []),
        ...(BuildXLSdk.isFullFramework || qualifier.targetFramework === "netstandard2.0" ? [
            importFrom("System.Memory").withQualifier({targetFramework: "netstandard2.0"}).pkg,
        ] : []),
        importFrom("System.Text.Encodings.Web").withQualifier({targetFramework: "netstandard2.0"}).pkg,
        importFrom("System.Numerics.Vectors").withQualifier({targetFramework: "netstandard2.0"}).pkg,
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
            qualifier.targetFramework !== "net6.0",
            importFrom("System.Text.Json").withQualifier({targetFramework: "netstandard2.0"}).pkg),
    ];
}

@@public
export function getProtobufPackages() : Managed.ManagedNugetPackage[] {
    return [
       BuildXLSdk.isFullFramework || qualifier.targetFramework === "netstandard2.0" ?
            importFrom("System.Memory").withQualifier({ targetFramework: "netstandard2.0" }).pkg 
            : importFrom("System.Memory").pkg,
        BuildXLSdk.isFullFramework || qualifier.targetFramework === "netstandard2.0" ?
            importFrom("System.Buffers").withQualifier({ targetFramework: "netstandard2.0" }).pkg 
            : importFrom("System.Buffers").pkg,

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
         BuildXLSdk.isDotNetCoreApp
            ? importFrom("Grpc.Core.Api").withQualifier({ targetFramework: "netstandard2.1" }).pkg
            : importFrom("Grpc.Core.Api").pkg,
        ...BuildXLSdk.bclAsyncPackages,
    ];
}

@@public
export function getGrpcAspNetCorePackages() : (Managed.ManagedNugetPackage | Managed.Assembly)[] {
    return [
        ...addIfLazy(BuildXLSdk.isDotNetCoreBuild, () => [
                  importFrom("Grpc.Net.Common").pkg,
                  importFrom("Grpc.Net.Client").pkg,
                  importFrom("Grpc.Net.Client.Web").pkg,
                  importFrom("Grpc.Net.ClientFactory").pkg,
                 
                  importFrom("Grpc.AspNetCore.Server.ClientFactory").pkg,
                  importFrom("Grpc.AspNetCore.Server").pkg,
                  
                  BuildXLSdk.withWinRuntime(importFrom("System.Security.Cryptography.ProtectedData").pkg, r`runtimes/win/lib/netstandard2.0`),
                  
                  // AspNetCore assemblies
                  Managed.Factory.filterRuntimeSpecificBinaries(BuildXLSdk.WebFramework.getFrameworkPackage(), [
                    importFrom("System.IO.Pipelines").pkg
                  ])
        ])
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
        importFrom("WindowsAzure.Storage").pkg,
        importFrom("Azure.Storage.Blobs").pkg,
        importFrom("Azure.Storage.Common").pkg,
        importFrom("Azure.Core").pkg,
        importFrom("Azure.Storage.Blobs.Batch").pkg,
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
