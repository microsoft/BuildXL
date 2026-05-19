// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// TODO: This project was only deployed for net472 (not .NET Core) since the initial open-source sync (2019).
// It was never included in .NET Core builds of bxl.exe, which means it likely hasn't been exercised in production
// for years. Consider whether this cache filter layer is still needed or can be removed entirely.

namespace InputListFilter {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.InputListFilter",
        sources: globR(d`.`, "*.cs"),
        references: [
            ImplementationSupport.dll,
            Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
        ],
    });
}
