// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BasicFilesystem{
    export declare const qualifier: BuildXLSdk.DefaultQualifier;
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "BuildXL.Cache.BasicFilesystem.Test",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Cache.VerticalStore").BasicFilesystem.dll,
            importFrom("BuildXL.Cache.VerticalStore").ImplementationSupport.dll,
            importFrom("BuildXL.Cache.VerticalStore").Interfaces.dll,
            importFrom("BuildXL.Cache.VerticalStore").InMemory.dll,
            importFrom("BuildXL.Cache.VerticalStore").VerticalAggregator.dll,
            Interfaces.dll,
            VerticalAggregator.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Utilities").dll,
        ],
        // Increase the weight of these tests since they do more I/O
        runTestArgs: { weight: 2},
    });
}
