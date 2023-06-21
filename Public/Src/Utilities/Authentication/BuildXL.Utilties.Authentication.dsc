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
            importFrom("Microsoft.Identity.Client").pkg,
            // TODO: this should be part of Microsoft.Identity.Client dependencies, but there seems to be a bug in the spec generation and the dependency is left out.
            importFrom("Microsoft.IdentityModel.Abstractions").pkg,
            importFrom("Microsoft.Identity.Client.Extensions.Msal").pkg,
            importFrom("Microsoft.VisualStudio.Services.Client").pkg,
            importFrom("Microsoft.VisualStudio.Services.InteractiveClient").pkg,
        ],
        internalsVisibleTo: [
            "Test.BuildXL.Utilities.Authentication"
        ]
    });
}