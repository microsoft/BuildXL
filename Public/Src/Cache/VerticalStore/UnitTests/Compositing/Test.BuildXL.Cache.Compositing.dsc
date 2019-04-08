// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Compositing {
    export const dll = BuildXLSdk.test({
        assemblyName: "BuildXL.Cache.Compositing.Test",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Cache.VerticalStore").Compositing.dll,
            importFrom("BuildXL.Cache.VerticalStore").ImplementationSupport.dll,
            importFrom("BuildXL.Cache.VerticalStore").Interfaces.dll,
            importFrom("BuildXL.Cache.VerticalStore").InMemory.dll,
            importFrom("BuildXL.Cache.VerticalStore").VerticalAggregator.dll,
            Interfaces.dll,
            VerticalAggregator.dll,
            importFrom("BuildXL.Utilities").dll,
        ],
    });
}
