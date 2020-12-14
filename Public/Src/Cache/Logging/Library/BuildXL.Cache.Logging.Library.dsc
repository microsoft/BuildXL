// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Library {
    export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet472AndNetStandard20;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.Logging",
        sources: globR(d`.`,"*.cs"),
        references: [
            importFrom("WindowsAzure.Storage").pkg,
            importFrom("NLog").pkg,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Library.dll,
            importFrom("BuildXL.Utilities").dll,
            ...BuildXLSdk.systemThreadingTasksDataflowPackageReference,
        ],
        internalsVisibleTo: [
            "BuildXL.Cache.Logging.Test"
        ]
    });
}
