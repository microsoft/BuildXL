// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXLTask {
    // Genuine net472 MSBuild task — must keep net472
    export declare const qualifier: BuildXLSdk.FullFrameworkQualifier;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.IDE.BuildXLTask",
        rootNamespace: "BuildXL.IDE.BuildXLTask",
        sources: globR(d`.`, "*.cs"),
        embeddedResources: [{resX: f`Strings.resx`}],
        references: [
            NetFx.Microsoft.Build.dll,
            NetFx.Microsoft.Build.Framework.dll,
            NetFx.Microsoft.Build.Utilities.V40.dll,
        ],
    });
}
