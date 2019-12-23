// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Configuration {

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Utilities.Configuration",
        sources: globR(d`.`, "*.cs"),
        embeddedResources: [
            {
                resX: f`Strings.resx`,
            }
        ],
        references: [
            $.dll,
            Interop.dll,
            $.Collections.dll,
        ],
    });
}
