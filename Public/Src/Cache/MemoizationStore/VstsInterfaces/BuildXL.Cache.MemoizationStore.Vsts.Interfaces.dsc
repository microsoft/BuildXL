// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VstsInterfaces {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.MemoizationStore.VstsInterfaces",
        sources: globR(d`.`, "*.cs"),
        references: [
            ContentStore.UtilitiesCore.dll,
            ContentStore.Hashing.dll,
            ContentStore.Interfaces.dll,
            ContentStore.VstsInterfaces.dll,
            Interfaces.dll,
            NetFx.System.Runtime.Serialization.dll,
            importFrom("Newtonsoft.Json.v10").pkg,
        ],
    });
}
