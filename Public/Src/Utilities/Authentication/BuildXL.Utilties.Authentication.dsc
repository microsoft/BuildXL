// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Authentication {
    @@public
    export const dll = !BuildXLSdk.Flags.isMicrosoftInternal ? undefined : BuildXLSdk.library({
        assemblyName: "BuildXL.Utilities.Authentication",
        sources: globR(d`.`, "*.cs"),
        references: [
            Utilities.Core.dll,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("Microsoft.Artifacts.Authentication").pkg,
            importFrom("Microsoft.Extensions.Logging.Abstractions").pkg,
            importFrom("Microsoft.Identity.Client.Broker").pkg,
            importFrom("Microsoft.Identity.Client.NativeInterop").pkg,
            importFrom("Microsoft.Identity.Client").pkg,
            // TODO: this should be part of Microsoft.Identity.Client dependencies, but there seems to be a bug in the spec generation and the dependency is left out.
            importFrom("Microsoft.IdentityModel.Abstractions").pkg,
            importFrom("Microsoft.Identity.Client.Extensions.Msal").pkg,
            importFrom("System.IO.FileSystem.AccessControl").pkg,
            importFrom("System.Security.Cryptography.ProtectedData").pkg,
            importFrom("Microsoft.VisualStudio.Services.Client").pkg,
            importFrom("Microsoft.VisualStudio.Services.InteractiveClient").pkg,
            ...addIfLazy(BuildXLSdk.isFullFramework, () => [
                NetFx.Netstandard.dll
            ]),
        ],
        internalsVisibleTo: [
            "Test.BuildXL.Utilities.Authentication"
        ],
        runtimeContent: [
            importFrom("Microsoft.Identity.Client.NativeInterop").Contents.all.getFile(r`runtimes/win-x64/native/msalruntime.dll`),,
        ]
    });
}