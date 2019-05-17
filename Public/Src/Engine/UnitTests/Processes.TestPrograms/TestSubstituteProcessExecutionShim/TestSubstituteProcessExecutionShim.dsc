// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as Deployment from "Sdk.Deployment";

namespace TestSubstituteProcessExecutionShim {
    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "TestSubstituteProcessExecutionShim",
        skipDocumentationGeneration: true,
        sources: [f`TestSubstituteProcessExecutionShimProgram.cs`],
    });
}
