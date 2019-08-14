// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";
import * as MemoizationStore from "BuildXL.Cache.MemoizationStore";

export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet451;

export {BuildXLSdk};

export const NetFx = BuildXLSdk.NetFx;

namespace Default {
    export declare const qualifier: BuildXLSdk.DefaultQualifierWithNet451AndNetStandard20;

    @@public
    export const deployment: Deployment.Definition =
    {
        contents: [
            {
                subfolder: r`App`,
                contents: [
                    App.exe,
                    // Back-Compat naming
                    {
                        file: App.exe.runtime.binary,
                        targetFileName: "Microsoft.ContentStoreApp.exe",
                    },
                    {
                        file: App.exe.runtime.pdb,
                        targetFileName: "Microsoft.ContentStoreApp.pdb",
                    }
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
        {
            file: App.exe.runtime.binary,
            targetFileName: "Microsoft.ContentStoreApp.exe",
        },
        {
            file: App.exe.runtime.pdb,
            targetFileName: "Microsoft.ContentStoreApp.pdb",
        },

        importFrom("Grpc.Core").pkg,
        importFrom("Google.Protobuf").pkg,

        ...addIf(qualifier.targetRuntime === "win-x64",
            importFrom("Grpc.Core").Contents.all.getFile("runtimes/win/native/grpc_csharp_ext.x64.dll"),
            importFrom("Grpc.Core").Contents.all.getFile("runtimes/win/native/grpc_csharp_ext.x86.dll")),
        ...addIf(qualifier.targetRuntime === "osx-x64",
            importFrom("Grpc.Core").Contents.all.getFile("runtimes/osx/native/libgrpc_csharp_ext.x64.dylib"),
            importFrom("Grpc.Core").Contents.all.getFile("runtimes/osx/native/libgrpc_csharp_ext.x86.dylib")),

        importFrom("TransientFaultHandling.Core").Contents.all.getFile("lib/NET4/Microsoft.Practices.TransientFaultHandling.Core.dll"),
    ]
};
