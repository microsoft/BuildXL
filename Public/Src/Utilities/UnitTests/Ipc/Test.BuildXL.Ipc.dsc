// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Ipc {
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.Ipc",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("Microsoft.Bcl.HashCode").pkg,
            importFrom("Microsoft.ManifestGenerator").pkg,
            ...BuildXLSdk.systemThreadingTasksDataflowPackageReference,
            ...addIf(
                BuildXLSdk.isFullFramework,
                NetFx.Netstandard.dll
            )
        ],
    });
}
