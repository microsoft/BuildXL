// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
import * as ManagedSdk from "Sdk.Managed";
import { NetFx } from "Sdk.BuildXL";

namespace App {
    @@public
    export const AppRuleset = f`ContentStoreApp.ruleset`;

    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "ContentStoreApp",
        sources: globR(d`.`,"*.cs"),
        skipDocumentationGeneration: true,
        appConfig: f`App.Config`,
        references: [
            ...(BuildXLSdk.isDotNetCoreBuild ? [
                importFrom("CLAP").withQualifier({targetFramework:"net451"}).pkg,
                importFrom("Microsoft.Azure.Kusto.Data.NETStandard").pkg,
                importFrom("Microsoft.Azure.Kusto.Ingest.NETStandard").pkg,
                importFrom("Microsoft.Azure.Kusto.Cloud.Platform.Azure.NETStandard").pkg,
                importFrom("Microsoft.Azure.Kusto.Cloud.Platform.NETStandard").pkg,
                importFrom("Microsoft.Extensions.PlatformAbstractions").withQualifier({targetFramework: "net472"}).pkg,
            ] : [
                importFrom("CLAP").pkg,
                importFrom("Microsoft.Azure.Kusto.Ingest").withQualifier({targetFramework: "net462"}).pkg,
            ]
            ),
            UtilitiesCore.dll,
            Grpc.dll,
            Hashing.dll,
            Library.dll,
            Distributed.dll,
            Interfaces.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Distributed.dll,
            importFrom("BuildXL.Cache.DistributedCache.Host").Service.dll,
            importFrom("BuildXL.Cache.DistributedCache.Host").Configuration.dll,

            importFrom("Grpc.Core").pkg,
            importFrom("Google.Protobuf").pkg,
            importFrom("Microsoft.IdentityModel.Clients.ActiveDirectory").pkg,
            importFrom("Newtonsoft.Json").pkg,

            ManagedSdk.Factory.createBinary(importFrom("TransientFaultHandling.Core").Contents.all, r`lib/NET4/Microsoft.Practices.TransientFaultHandling.Core.dll`),
        ],
        tools: {
            csc: {
                keyFile: undefined, // This must be unsigned so it can consume CLAP
            },
        },
        runtimeContent: [
            importFrom("Sdk.SelfHost.Sqlite").runtimeLibs,
        ]
    });
}
