// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as ContentStore from "BuildXL.Cache.ContentStore";

namespace Library {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.Monitor.Library",
        sources: globR(d`.`,"*.cs"),
        references: [
            ...importFrom("BuildXL.Cache.ContentStore").kustoPackages,
            ...importFrom("BuildXL.Cache.ContentStore").getSerializationPackages(true),

            importFrom("System.Collections.Immutable").pkg,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,

            ContentStore.Library.dll,
            ContentStore.Interfaces.dll,

            importFrom("Newtonsoft.Json").pkg,

            importFrom("RuntimeContracts").pkg,
            ...azureSdk,
        ],
        internalsVisibleTo: [
            "BuildXL.Cache.Monitor.App", 
            "BuildXL.Cache.Monitor.Test",
        ],
        skipDocumentationGeneration: true,
        nullable: true,
    });
}
