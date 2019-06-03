// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Compositing {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.Compositing",
        sources: globR(d`.`, "*.cs"),
        references: [
            ImplementationSupport.dll,
            Interfaces.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
        ],
        internalsVisibleTo: [
            "BuildXL.Cache.Compositing.Test",
        ],
    });
}
