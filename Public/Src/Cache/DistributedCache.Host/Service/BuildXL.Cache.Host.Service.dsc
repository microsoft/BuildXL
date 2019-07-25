// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Service {

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.Host.Service",
        sources: globR(d`.`,"*.cs"),
        skipDocumentationGeneration: true,
        references: [
            Configuration.dll,
            importFrom("BuildXL.Cache.ContentStore").Library.dll,
            importFrom("BuildXL.Cache.ContentStore").Distributed.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            BuildXLSdk.Factory.createBinary(importFrom("TransientFaultHandling.Core").Contents.all, r`lib/NET4/Microsoft.Practices.TransientFaultHandling.Core.dll`),

            importFrom("WindowsAzure.Storage").pkg,
        ],
        allowUnsafeBlocks: false
    });
}
