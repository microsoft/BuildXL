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
            importFrom("CLAP-DotNetCore").pkg,            
            ContentStore.Library.dll,
            ContentStore.Interfaces.dll,
            Library.dll,
            importFrom("Newtonsoft.Json").pkg,
        ],
        tools: {
            csc: {
                keyFile: undefined, // This must be unsigned so it can consume CLAP
            },
        },
        skipDocumentationGeneration: true,
        nullable: true,
    });
}
