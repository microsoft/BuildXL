// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";

namespace Test.Tool.CloudTestClient {
    export declare const qualifier: BuildXLSdk.Net9Qualifier;

    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.Tool.CloudTestClient",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Tools").CloudTestClient.tool,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
        ],
    });
}