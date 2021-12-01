// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Test.Tool.SBOMConverter {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    @@public
    export const dll = !BuildXLSdk.isDropToolingEnabled ? undefined : BuildXLSdk.test({
        assemblyName: "Test.Tool.SBOMConverter",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("Newtonsoft.Json").pkg,
            importFrom("BuildXL.Tools").SBOMConverter.exe,
            importFrom("Microsoft.SBOMApi").pkg,
            // TODO: Uncomment this and remove SBOMApi once newer versions are stable
            //importFrom("Microsoft.Sbom.Contracts").pkg,
        ]
    });
}