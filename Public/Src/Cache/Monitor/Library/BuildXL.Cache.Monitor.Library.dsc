// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as ContentStore from "BuildXL.Cache.ContentStore";

namespace Library {
    @@public
    export const dll =  !BuildXLSdk.Flags.isMicrosoftInternal ? undefined : BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.Monitor.Library",
        sources: globR(d`.`,"*.cs"),
        references: [
            ...importFrom("BuildXL.Cache.ContentStore").kustoPackages,
            ...importFrom("BuildXL.Cache.ContentStore").getSerializationPackages(true),

            importFrom("System.Collections.Immutable").pkg,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Native.dll,

            ContentStore.Distributed.dll,
            ContentStore.Library.dll,
            ContentStore.Interfaces.dll,

            importFrom("Newtonsoft.Json").pkg,

            importFrom("RuntimeContracts").pkg,
            ...azureSdk,

            // IcM
            importFrom("Microsoft.AzureAd.Icm.Types.amd64").pkg,
            importFrom("Microsoft.AzureAd.Icm.WebService.Client.amd64").pkg,
            importFrom("System.ServiceModel.Primitives").pkg,
            importFrom("System.Private.ServiceModel").pkg,
            importFrom("System.ServiceModel.Http").pkg,

            importFrom("Azure.Identity").pkg,
            importFrom("Azure.Core").pkg,
            importFrom("Azure.Security.KeyVault.Secrets").pkg,
        ],
        internalsVisibleTo: [
            "BuildXL.Cache.Monitor.App", 
            {
                assembly: "BuildXL.Cache.Monitor.Test",
            }
        ],
        skipDocumentationGeneration: true,
        nullable: true,
        tools: {
            csc: {
                keyFile: undefined, // This must be unsigned so it can consume IcM
            }
        },
    });
}
