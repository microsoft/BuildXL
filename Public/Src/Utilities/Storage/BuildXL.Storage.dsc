// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
namespace Storage {

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Storage",
        sources: globR(d`.`, "*.cs"),
        generateLogs: true,
        references: [
            $.dll,
            Native.dll,
            Collections.dll,
            Configuration.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
        ],
        embeddedResources: [{resX: f`Strings.resx`}],
        internalsVisibleTo: [
            "Test.BuildXL.Storage",
        ],
    });
}
