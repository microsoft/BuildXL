// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as ContentStore from "BuildXL.Cache.ContentStore";

namespace App {
    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "BuildXL.Cache.Monitor.App",
        sources: globR(d`.`,"*.cs"),
        appConfig: f`App.config`,
        references: [
            // CLAP only exists for full framework net35. Ignoring the fact that this doesn't work on netcoreapp
            importFrom("CLAP").withQualifier({targetFramework:"net472"}).pkg, 

            importFrom("System.Interactive.Async").pkg,

            importFrom("Microsoft.Azure.Kusto.Ingest").pkg,

            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,

            ContentStore.Library.dll,
            ContentStore.Interfaces.dll,
        ],
    });
}
