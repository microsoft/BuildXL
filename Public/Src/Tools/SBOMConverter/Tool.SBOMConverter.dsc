// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as Deployment from "Sdk.Deployment";

namespace SBOMConverter {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    @@public
    export const exe = !(BuildXLSdk.isDropToolingEnabled && Context.getCurrentHost().os === "win") ? undefined : BuildXLSdk.executable({
        assemblyName: "SBOMConverter",
        rootNamespace: "Tool.SBOMConverter",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("Newtonsoft.Json").pkg,
            importFrom("Microsoft.VisualStudio.Services.Governance.ComponentDetection.Contracts").withQualifier({ targetFramework: "netstandard2.1" }).pkg,
            importFrom("PackageUrl").pkg,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("Microsoft.Sbom.Contracts").pkg,
        ],
        tools: {
            csc: {
                // PackageUrl and Microsoft.VisualStudio.Services.Governance.ComponentDetection.Contracts do not have strong assembly names
                noWarnings: [8002]
            }
        }
    });

    @@public
    export const deployment : Deployment.Definition = {
        contents: [{
            subfolder: r`tools/SBOMConverter`,
            contents: [exe]
        }]
    };
}