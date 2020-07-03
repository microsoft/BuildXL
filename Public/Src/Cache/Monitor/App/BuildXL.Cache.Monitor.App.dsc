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
                importFrom("CLAP-DotNetCore").pkg,
            ] : [
                NetFx.System.Data.dll,
                importFrom("CLAP").pkg,
            ]
            ),
            ...importFrom("BuildXL.Cache.ContentStore").kustoPackages,

            importFrom("System.Collections.Immutable").pkg,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,

            ContentStore.Library.dll,
            ContentStore.Interfaces.dll,

            importFrom("Newtonsoft.Json").pkg,
        ],
        tools: {
            csc: {
                keyFile: undefined, // This must be unsigned so it can consume CLAP
            },
        },
    });
}
