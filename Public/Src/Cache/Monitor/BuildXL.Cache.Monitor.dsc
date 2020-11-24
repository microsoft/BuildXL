// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";

export declare const qualifier : BuildXLSdk.NetCoreAppQualifier;

export {BuildXLSdk};

@@public
export const azureSdk = [
    // Required for Azure Authentication
    importFrom("Microsoft.Rest.ClientRuntime").pkg,
    importFrom("Microsoft.Rest.ClientRuntime.Azure").pkg,
    importFrom("Microsoft.Rest.ClientRuntime.Azure.Authentication").pkg,
    importFrom("Microsoft.IdentityModel.Clients.ActiveDirectory").pkg,

    // Useless stuff they force us to bring in
    importFrom("Microsoft.Azure.Management.AppService.Fluent").pkg,
    importFrom("Microsoft.Azure.Management.BatchAI.Fluent").pkg,
    importFrom("Microsoft.Azure.Management.Cdn.Fluent").pkg,
    importFrom("Microsoft.Azure.Management.Compute.Fluent").pkg,
    importFrom("Microsoft.Azure.Management.ContainerInstance.Fluent").pkg,
    importFrom("Microsoft.Azure.Management.ContainerRegistry.Fluent").pkg,
    importFrom("Microsoft.Azure.Management.ContainerService.Fluent").pkg,
    importFrom("Microsoft.Azure.Management.CosmosDB.Fluent").pkg,
    importFrom("Microsoft.Azure.Management.Dns.Fluent").pkg,
    importFrom("Microsoft.Azure.Management.EventHub.Fluent").pkg,
    importFrom("Microsoft.Azure.Management.Graph.RBAC.Fluent").pkg,
    importFrom("Microsoft.Azure.Management.KeyVault.Fluent").pkg,
    importFrom("Microsoft.Azure.Management.Locks.Fluent").pkg,
    importFrom("Microsoft.Azure.Management.Msi.Fluent").pkg,
    importFrom("Microsoft.Azure.Management.Network.Fluent").pkg,
    importFrom("Microsoft.Azure.Management.PrivateDns.Fluent").pkg,
    importFrom("Microsoft.Azure.Management.Search.Fluent").pkg,
    importFrom("Microsoft.Azure.Management.ServiceBus.Fluent").pkg,
    importFrom("Microsoft.Azure.Management.Sql.Fluent").pkg,
    importFrom("Microsoft.Azure.Management.Storage.Fluent").pkg,
    importFrom("Microsoft.Azure.Management.TrafficManager.Fluent").pkg,

    // These are the actual packages we care about
    importFrom("Microsoft.Azure.Management.Redis").pkg,
    importFrom("Microsoft.Azure.Management.Redis.Fluent").pkg,
    importFrom("Microsoft.Azure.Management.ResourceManager.Fluent").pkg,
    importFrom("Microsoft.Azure.Management.Fluent").pkg,
    importFrom("Microsoft.Azure.Management.Monitor.Fluent").pkg,
    importFrom("Microsoft.Azure.Management.Monitor").pkg,
];

namespace Default {
    @@public
    export const deployment: Deployment.Definition = !BuildXLSdk.Flags.isMicrosoftInternal ? undefined :
    {
        contents: [
            {
                subfolder: r`App`,
                contents: [
                    App.exe
                ]
            },
        ]
    };
}
