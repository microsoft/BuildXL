// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Ipc {
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.Ipc",
        appConfig: f`App.config`,
        assemblyBindingRedirects: BuildXLSdk.cacheBindingRedirects(),
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").Ipc.Providers.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            ...BuildXLSdk.systemThreadingTasksDataflowPackageReference,
            ...addIf(
                BuildXLSdk.isFullFramework,
                NetFx.Netstandard.dll
            )
        ],
    });
}
