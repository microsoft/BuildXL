// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VstsAuthentication {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Utilities.VstsAuthentication",
        sources: globR(d`.`, "*.cs"),
        references: [
            $.dll,
            Native.dll,
            Utilities.Core.dll,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("NuGet.Versioning").pkg,
            importFrom("NuGet.Protocol").pkg,
            importFrom("NuGet.Configuration").pkg,
            importFrom("NuGet.Common").pkg,
            importFrom("NuGet.Frameworks").pkg,
            importFrom("NuGet.Packaging").pkg,
            ...addIfLazy(BuildXLSdk.isFullFramework, () => [
                NetFx.System.Net.Http.dll,
                NetFx.Netstandard.dll,
            ]),
        ],
    });
}