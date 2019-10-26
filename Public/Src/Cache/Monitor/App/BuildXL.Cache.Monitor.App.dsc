// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as ContentStore from "BuildXL.Cache.ContentStore";

export const NetFx = BuildXLSdk.NetFx;

namespace App {
    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "BuildXL.Cache.Monitor.App",
        sources: globR(d`.`,"*.cs"),
        appConfig: f`App.config`,
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Data.dll
            ),


            // CLAP only exists for full framework net35. Ignoring the fact that this doesn't work on netcoreapp
            importFrom("CLAP").withQualifier({targetFramework:"net472"}).pkg, 

            importFrom("System.Interactive.Async").pkg,

            importFrom("System.Collections.Immutable").pkg,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,

            ContentStore.Library.dll,
            ContentStore.Interfaces.dll,

            // Required to communicate with Kusto
            ...(BuildXLSdk.isDotNetCoreBuild ? [
                importFrom("Microsoft.Azure.Kusto.Data.NETStandard").pkg,
                importFrom("Microsoft.Azure.Kusto.Ingest.NETStandard").pkg,
                importFrom("Microsoft.Azure.Kusto.Cloud.Platform.Azure.NETStandard").pkg,
                importFrom("Microsoft.Azure.Kusto.Cloud.Platform.NETStandard").pkg,
                importFrom("Microsoft.Extensions.PlatformAbstractions").withQualifier({targetFramework: "net472"}).pkg,

                importFrom("Microsoft.IO.RecyclableMemoryStream").pkg,
            ] : [
                importFrom("Microsoft.Azure.Kusto.Ingest").withQualifier({targetFramework: "net472"}).pkg,
            ]
            ),

            importFrom("Microsoft.Azure.Management.Kusto").pkg,
            importFrom("System.Data.Common").pkg,
            importFrom("Microsoft.IdentityModel.Clients.ActiveDirectory").pkg,

            importFrom("Newtonsoft.Json").pkg,
            importFrom("WindowsAzure.Storage").pkg,
        ],
    });
}
