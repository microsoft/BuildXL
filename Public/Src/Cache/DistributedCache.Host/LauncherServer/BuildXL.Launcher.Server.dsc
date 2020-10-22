// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as BuildXLSdk from "Sdk.BuildXL";
import { NetFx } from "Sdk.BuildXL";

namespace LauncherServer {

    export declare const qualifier: BuildXLSdk.NetCoreAppQualifier;

    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "CacheService",
        skipDocumentationGeneration: true,
        deployRuntimeConfigFile: true,
       // We filter out obj and bin folders since we sometimes still develop with an msbuild file for F5 debugging of aspnet apps which is not yet available in BuildXL's IDE integraiotn.
        sources: (<File[]>globR(d`.`, "*.cs")).filter(f => !(<File>f).isWithin(d`obj`) && !(<File>f).isWithin(d`bin`)),
        references: [
            Configuration.dll,
            Service.dll,

            importFrom("BuildXL.Cache.ContentStore").Library.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,

            importFrom("Azure.Security.KeyVault.Secrets").pkg,
            importFrom("Azure.Identity").pkg,
            importFrom("Azure.Core").pkg,
            importFrom("Microsoft.Identity.Client").pkg,


            // AspNetCore assemblies
            Managed.Factory.filterRuntimeSpecificBinaries(BuildXLSdk.WebFramework.getFrameworkPackage(), [
                importFrom("System.IO.Pipelines").pkg
            ])
        ],
        assemblyBindingRedirects: [
            {
                name: "System.IO.Pipelines",
                publicKeyToken: "cc7b13ffcd2ddd51",
                culture: "neutral",
                oldVersion: "0.0.0.0-5.0.0.0",
                newVersion: "4.0.2.0",
            },
        ]
    });
}
