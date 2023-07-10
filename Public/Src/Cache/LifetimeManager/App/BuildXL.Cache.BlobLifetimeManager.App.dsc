// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace App {
    @@public
    export const exe = !BuildXLSdk.Flags.isMicrosoftInternal ? undefined : BuildXLSdk.executable({
        assemblyName: "BuildXL.Cache.BlobLifetimeManager",
        sources: globR(d`.`,"*.cs"),
        references: [
            importFrom("CLAP-DotNetCore").pkg,
            Library.dll,

            importFrom("BuildXL.Cache.ContentStore").Distributed.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").Library.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
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
