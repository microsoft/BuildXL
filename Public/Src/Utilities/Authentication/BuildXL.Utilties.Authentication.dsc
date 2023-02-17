// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Authentication {
    @@public
    export const dll = !BuildXLSdk.Flags.isMicrosoftInternal ? undefined : BuildXLSdk.library({
        assemblyName: "BuildXL.Utilities.Authentication",
        sources: globR(d`.`, "*.cs"),
        references: [
            $.dll,
            Utilities.Core.dll,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("Microsoft.Identity.Client").pkg,
            importFrom("Microsoft.Identity.Client.Extensions.Msal").pkg,
            importFrom("Microsoft.IdentityModel.Clients.ActiveDirectory").pkg,
            importFrom("Microsoft.VisualStudio.Services.Client").pkg,
            importFrom("Microsoft.VisualStudio.Services.InteractiveClient").pkg,
        ],
        internalsVisibleTo: [
            "Test.BuildXL.Utilities.Authentication"
        ]
    });
}