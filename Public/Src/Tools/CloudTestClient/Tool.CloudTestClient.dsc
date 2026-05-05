// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";

namespace CloudTestClient {

    export declare const qualifier: BuildXLSdk.Net9Qualifier;

    @@public
    export const tool = BuildXLSdk.executable({
        assemblyName: "CloudTestClient",
        skipDocumentationGeneration: true,
        skipDefaultReferences: true,
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
        ],
    });
}
