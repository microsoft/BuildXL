// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Native.Extensions {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Native.Extensions",
        sources: globR(d`.`, "*.cs"),
        nullable: true,
        references: [
            Native.dll,
            Utilities.Core.dll,
            importFrom("CopyOnWrite").pkg,
        ],
    });
}
