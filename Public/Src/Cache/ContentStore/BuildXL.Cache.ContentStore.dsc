// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";
import * as MemoizationStore from "BuildXL.Cache.MemoizationStore";

export declare const qualifier : BuildXLSdk.AllSupportedQualifiers;

export {BuildXLSdk};

export const NetFx = BuildXLSdk.NetFx;

@@public
export const redisPackages = [
    importFrom("StackExchange.Redis").pkg,
    ...(BuildXLSdk.isFullFramework 
        ? [ 
            // Needed because net472 -> netstandard2.0 translation is not yet supported by the NuGet resolver.
            importFrom("System.IO.Pipelines").withQualifier({ targetFramework: "netstandard2.0" }).pkg,
            importFrom("System.Threading.Channels").withQualifier({ targetFramework: "netstandard2.0" }).pkg,
            importFrom("System.Runtime.CompilerServices.Unsafe").withQualifier({ targetFramework: "netstandard2.0" }).pkg,
            importFrom("Pipelines.Sockets.Unofficial").withQualifier({ targetFramework: "netstandard2.0" }).pkg,
          ] 
        : [
            importFrom("System.IO.Pipelines").pkg,
            importFrom("System.Threading.Channels").pkg,
            // Don't need to add Unsafe for net5.0
            ...(qualifier.targetFramework === "net5.0" ? [] : [importFrom("System.Runtime.CompilerServices.Unsafe").pkg]),
            importFrom("Pipelines.Sockets.Unofficial").pkg,
          ]),
    ...BuildXLSdk.bclAsyncPackages,
    // Needed because of snipped dependencies for System.IO.Pipelines and System.Threading.Channels
    importFrom("System.Threading.Tasks.Extensions").pkg,
];

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
    importFrom("WindowsAzure.Storage").pkg
];

// Need to exclude netstandard.dll reference when calling this function for creating a nuget package.
@@public
export function getSerializationPackages(includeNetStandard: boolean) {
    return [
        ...(includeNetStandard && BuildXLSdk.isFullFramework ? [
            BuildXLSdk.withQualifier({targetFramework: "net472"}).NetFx.Netstandard.dll,
        ] : [
        ]),
        
        ...(qualifier.targetFramework === "net5.0" ? [] : [importFrom("System.Text.Json").withQualifier({targetFramework: "netstandard2.0"}).pkg]),

        importFrom("System.Memory").withQualifier({targetFramework: "netstandard2.0"}).pkg,
        importFrom("System.Text.Encodings.Web").withQualifier({targetFramework: "netstandard2.0"}).pkg,
        importFrom("System.Numerics.Vectors").withQualifier({targetFramework: "netstandard2.0"}).pkg,
    ];
}

@@public
export function getProtobufPackages(includeNetStandard: boolean) {
    return [
        ...(BuildXLSdk.isFullFramework && includeNetStandard ? [
                NetFx.System.IO.dll,

                ...(qualifier.targetFramework === "net462" ? [
                    // HACK: Net462 doesn't ship with netstandard dlls, so we fetch them from Net472 instead. This
                    // may not work.
                    importFrom("Sdk.Managed.Frameworks.Net472").withQualifier({targetFramework: "net472"}).NetFx.Netstandard.dll
                ] : [
                    NetFx.Netstandard.dll,
                ])
            ] : []
        ),

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
export function getGrpcPackages(includeNetStandard: boolean) {
    return [
        ...getProtobufPackages(includeNetStandard),
        importFrom("Grpc.Core").pkg,
        importFrom("Grpc.Core.Api").pkg,
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
            importFrom("Grpc.Core").Contents.all.getFile("runtimes/win/native/grpc_csharp_ext.x64.dll"),
            importFrom("Grpc.Core").Contents.all.getFile("runtimes/win/native/grpc_csharp_ext.x86.dll")),
        ...addIf(qualifier.targetRuntime === "osx-x64",
            importFrom("Grpc.Core").Contents.all.getFile("runtimes/osx/native/libgrpc_csharp_ext.x64.dylib"),
            importFrom("Grpc.Core").Contents.all.getFile("runtimes/osx/native/libgrpc_csharp_ext.x86.dylib")),
    ]
};
