// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
namespace Storage {
    export declare const qualifier: BuildXLSdk.DefaultQualifierWithNet6AndNet472;
    
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Storage",
        sources: globR(d`.`, "*.cs"),
        generateLogs: true,
        addNotNullAttributeFile: true,
        references: [
            $.dll,
            Native.dll,
            Configuration.dll,
            Utilities.Core.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Utilities.Instrumentation").AriaCommon.dll,

            ...BuildXLSdk.systemMemoryDeployment,
        ],
        embeddedResources: [{resX: f`Strings.resx`}],
        internalsVisibleTo: [
            "Test.BuildXL.Storage",
        ],
    });
}
