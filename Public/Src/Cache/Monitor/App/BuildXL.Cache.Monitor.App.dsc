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
            ...(BuildXLSdk.isDotNetCoreBuild ? [
                importFrom("Microsoft.Azure.Kusto.Data.NETStandard").pkg,
                importFrom("Microsoft.Azure.Kusto.Ingest.NETStandard").pkg,
                importFrom("Microsoft.Azure.Kusto.Cloud.Platform.Azure.NETStandard").pkg,
                importFrom("Microsoft.Azure.Kusto.Cloud.Platform.NETStandard").pkg,
                importFrom("Microsoft.Extensions.PlatformAbstractions").pkg,

                importFrom("Microsoft.IO.RecyclableMemoryStream").pkg,

                importFrom("CLAP-DotNetCore").pkg,
            ] : [
                NetFx.System.Data.dll,
                importFrom("Microsoft.Azure.Kusto.Ingest").pkg,
                importFrom("CLAP").pkg,
            ]
            ),

            importFrom("System.Collections.Immutable").pkg,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,

            ContentStore.Library.dll,
            ContentStore.Interfaces.dll,

            importFrom("Microsoft.Azure.Management.Kusto").pkg,
            importFrom("Microsoft.IdentityModel.Clients.ActiveDirectory").pkg,

            importFrom("Newtonsoft.Json").pkg,
            importFrom("WindowsAzure.Storage").pkg
        ],
        tools: {
            csc: {
                keyFile: undefined, // This must be unsigned so it can consume CLAP
            },
        },
    });
}
