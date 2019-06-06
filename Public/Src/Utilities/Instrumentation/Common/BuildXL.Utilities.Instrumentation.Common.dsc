// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as MacServices from "BuildXL.Sandbox.MacOS";

namespace Common {
    @@public
    export const dll = BuildXLSdk.library({
        allowUnsafeBlocks: true,
        assemblyName: "BuildXL.Utilities.Instrumentation.Common",
        sources: globR(d`.`, '*.cs'),
        skipDefaultReferences: true,
        references: [
            ...addIfLazy(BuildXLSdk.isFullFramework, () => [
                importFrom("Microsoft.Applications.Telemetry.Desktop").pkg,
                importFrom("Microsoft.Diagnostics.Tracing.EventSource.Redist").pkg
            ]),
        ],
        runtimeContent: [
            ...addIfLazy(MacServices.Deployment.macBinaryUsage !== "none" && qualifier.targetRuntime === "osx-x64", () => [
                MacServices.Deployment.ariaLibrary
            ]),
        ],
        internalsVisibleTo: [
            "IntegrationTest.BuildXL.Scheduler",
        ],
    });
}
