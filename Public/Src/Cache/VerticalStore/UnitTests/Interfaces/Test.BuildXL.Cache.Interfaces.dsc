// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Interfaces {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;  
    @@public  
    export const dll = BuildXLSdk.test({
        assemblyName: "BuildXL.Cache.Interfaces.Test",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Cache.VerticalStore").ImplementationSupport.dll,
            importFrom("BuildXL.Cache.VerticalStore").InMemory.dll,
            importFrom("BuildXL.Cache.VerticalStore").Interfaces.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
        ],
    });
}
