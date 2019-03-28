// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace InputListFilter {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.InputListFilter",
        sources: globR(d`.`, "*.cs"),
        references: [
            ImplementationSupport.dll,
            Interfaces.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
        ],
    });
}
