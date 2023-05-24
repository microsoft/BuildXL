// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
import * as ManagedSdk from "Sdk.Managed";
import { NetFx } from "Sdk.BuildXL";

namespace App {
    export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet472;

    @@public
    export const AppRuleset = f`ContentStoreApp.ruleset`;

    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "ContentStoreApp",
        sources: globR(d`.`,"*.cs"),
        skipDocumentationGeneration: true,
        assemblyBindingRedirects: BuildXLSdk.cacheBindingRedirects(),
        appConfig: f`App.config`,
        references: [
            ...(BuildXLSdk.isDotNetCoreOrStandard ? [
                importFrom("CLAP-DotNetCore").pkg,
            ] : [
                importFrom("CLAP").pkg,
            ]
            ),
            ...kustoPackages,
            ...getSerializationPackages(true),
            ...getGrpcPackages(true),
            UtilitiesCore.dll,
            Grpc.dll,
            Hashing.dll,
            Library.dll,
            Distributed.dll,
            Interfaces.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Distributed.dll,
            importFrom("BuildXL.Cache.DistributedCache.Host").Service.dll,
            importFrom("BuildXL.Cache.DistributedCache.Host").Configuration.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            
            importFrom("Microsoft.IdentityModel.Clients.ActiveDirectory").pkg,
            importFrom("Newtonsoft.Json").pkg,

            ...getAzureBlobStorageSdkPackages(true),
            importFrom("Microsoft.Extensions.Logging.Abstractions.v6.0.0").pkg, // required by grpc.net packages
            ...BuildXLSdk.systemThreadingTasksDataflowPackageReference,
            importFrom("Microsoft.Bcl.AsyncInterfaces").pkg,
        ],
        tools: {
            csc: {
                keyFile: undefined, // This must be unsigned so it can consume CLAP
            },
        },
    });
}
