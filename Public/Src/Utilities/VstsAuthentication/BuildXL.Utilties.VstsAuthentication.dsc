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
            importFrom("Newtonsoft.Json").withQualifier({targetFramework: "netstandard2.0"}).pkg,
            importFrom("NuGet.Versioning").withQualifier({targetFramework: "netstandard2.0"}).pkg,
            importFrom("NuGet.Protocol").withQualifier({targetFramework: "netstandard2.0"}).pkg,
            importFrom("NuGet.Configuration").withQualifier({targetFramework: "netstandard2.0"}).pkg,
            importFrom("NuGet.Common").withQualifier({targetFramework: "netstandard2.0"}).pkg,
            importFrom("NuGet.Frameworks").withQualifier({targetFramework: "netstandard2.0"}).pkg,
            importFrom("NuGet.Packaging").withQualifier({targetFramework: "netstandard2.0"}).pkg,
            ...addIfLazy(BuildXLSdk.isFullFramework, () => [
                NetFx.System.Net.Http.dll,
                NetFx.Netstandard.dll,
            ]),
        ],
    });
}