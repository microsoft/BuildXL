// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as ContentStore from "BuildXL.Cache.ContentStore";

namespace App {
    export declare const qualifier : BuildXLSdk.DefaultQualifier;

    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "Roxis",
        sources: globR(d`.`,"*.cs"),
        appConfig: f`app.config`,
        references: [
            importFrom("CLAP-DotNetCore").pkg,

            Server.dll,
            Client.dll,
            Common.dll,

            importFrom("RuntimeContracts").pkg,

            ContentStore.Library.dll,
            ContentStore.Interfaces.dll,
            importFrom("BuildXL.Utilities").dll,
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
