// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";

namespace NinjaGraphBuilder {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "NinjaGraphBuilder",
        skipDocumentationGeneration: true,
        sources: globR(d`.`, "*.cs"),
        references:[
            importFrom("Newtonsoft.Json").pkg,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.FrontEnd").Ninja.Serialization.dll,
        ],
        internalsVisibleTo: [
            "Test.Tool.NinjaGraphBuilder",
        ]
    });
}
