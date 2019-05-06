// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BasicFilesystem {
    export declare const qualifier: BuildXLSdk.FullFrameworkQualifier;
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.BasicFilesystem",
        sources: globR(d`.`, "*.cs"),
        cacheOldNames: [{
            namespace: "BasicFilesystem",
            factoryClass: "BasicFilesystemCacheFactory",
        }],
        references: [
            ImplementationSupport.dll,
            Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("Newtonsoft.Json").pkg,
        ],
        internalsVisibleTo: [
            "BuildXL.Cache.BasicFilesystem.Test",
        ],
    });
}
