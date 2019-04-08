// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace InMemory {

    export declare const qualifier: BuildXLSdk.DefaultQualifier;
    
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.InMemory",
        sources: globR(d`.`, "*.cs"),
        cacheOldNames: [{
            namespace: "InMemory",
            factoryClass: "MemCacheFactory",
        }],
        references: [
            Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Utilities").dll,          
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
        ],
        internalsVisibleTo: [
            "BuildXL.Cache.Interfaces.Test",
            "BuildXL.Cache.Analyzer.Test",
        ],
    });
}
