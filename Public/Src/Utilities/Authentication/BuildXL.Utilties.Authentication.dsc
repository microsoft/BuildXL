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
            importFrom("Microsoft.Extensions.Logging.Abstractions.v6.0.0").pkg,
            importFrom("Microsoft.Identity.Client.Broker").pkg,
            importFrom("Microsoft.Identity.Client.NativeInterop").pkg,
            importFrom("Microsoft.Identity.Client").pkg,
            // TODO: this should be part of Microsoft.Identity.Client dependencies, but there seems to be a bug in the spec generation and the dependency is left out.
            importFrom("Microsoft.IdentityModel.Abstractions").pkg,
            importFrom("Microsoft.Identity.Client.Extensions.Msal").pkg,
            importFrom("Microsoft.IdentityModel.Clients.ActiveDirectory").pkg,
            importFrom("Microsoft.VisualStudio.Services.Client").pkg,
            importFrom("Microsoft.VisualStudio.Services.InteractiveClient").pkg,
            ...addIfLazy(BuildXLSdk.isFullFramework, () => [
                NetFx.Netstandard.dll
            ]),
        ],
        internalsVisibleTo: [
            "Test.BuildXL.Utilities.Authentication"
        ],
        runtimeContentToSkip : [
            importFrom("Microsoft.Extensions.Logging.Abstractions.v6.0.0").pkg,
        ],
        runtimeContent: [
            importFrom("Microsoft.Identity.Client.NativeInterop").Contents.all.getFile(r`runtimes/win-x64/native/msalruntime.dll`),,
        ]
    });
}