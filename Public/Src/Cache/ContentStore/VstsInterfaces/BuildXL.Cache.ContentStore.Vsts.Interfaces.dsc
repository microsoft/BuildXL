// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VstsInterfaces {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.ContentStore.VstsInterfaces",
        sources: globR(d`.`, "*.cs"),
        references: [
            BuildXLSdk.Factory.createBinary(importFrom("TransientFaultHandling.Core").Contents.all, r`lib/NET4/Microsoft.Practices.TransientFaultHandling.Core.dll`),
            
            Interfaces.dll,
            Hashing.dll,
            UtilitiesCore.dll,
        ],
    });
}
