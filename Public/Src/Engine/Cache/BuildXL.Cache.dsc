// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed  from "Sdk.Managed";

namespace Cache {
    @@public
    export const cacheBondSchemaPath: File = f`Fingerprints/PipCacheDescriptor.bond`;

    const openBondCacheDescriptorOutput = importFrom("Sdk.Protocols.Bond").generate({bondFile: cacheBondSchemaPath});
    const openBondDistributionOutput = importFrom("Sdk.Protocols.Bond").generate({bondFile: f`Fingerprints/OpenBondDistribution.bond`, includeFiles: [ cacheBondSchemaPath ]});

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Engine.Cache",
        generateLogs: true,
        sources: [
            ...globR(d`.`, "*.cs"),
            openBondCacheDescriptorOutput.csharpResult.typesFile,
            openBondDistributionOutput.csharpResult.typesFile,
        ],
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.IO.dll,
                NetFx.System.Text.Encoding.dll
            ),
            importFrom("Bond.Core.CSharp").pkg,
            importFrom("Bond.Runtime.CSharp").pkg,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("JsonDiffPatch.Net").pkg,
            importFrom("Newtonsoft.Json.v10").pkg,
        ],
        runtimeContentToSkip: [ importFrom("Newtonsoft.Json.v10").pkg ]
    });
}
