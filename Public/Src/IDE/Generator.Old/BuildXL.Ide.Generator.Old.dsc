// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
namespace Generator.Old {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Ide.Generator.Old",
        sources: globR(d`.`, "*.cs"),
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Xml.dll,
                NetFx.System.Xml.Linq.dll
            ),
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Branding.dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,
        ],
        embeddedResources: [
            {
                linkedContent: [
                    f`CommonBuildFiles/Common.props`,
                    f`CommonBuildFiles/CSharp.props`,
                    f`CommonBuildFiles/Common.targets`,
                    f`CommonBuildFiles/CSharp.targets`,
                    f`CommonBuildFiles/NuGet.config`,
                    f`CommonBuildFiles/packages.config`,
                ],
            },
        ],
    });
}
