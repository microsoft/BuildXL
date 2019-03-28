// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace InputListFilter {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "BuildXL.Cache.InputListFilter.Test",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Cache.VerticalStore").InputListFilter.dll,
            importFrom("BuildXL.Cache.VerticalStore").ImplementationSupport.dll,
            importFrom("BuildXL.Cache.VerticalStore").Interfaces.dll,
            importFrom("BuildXL.Cache.VerticalStore").InMemory.dll,
            importFrom("BuildXL.Cache.VerticalStore").VerticalAggregator.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            Interfaces.dll,
            VerticalAggregator.dll,
            importFrom("BuildXL.Utilities").dll,
        ],
    });
}
