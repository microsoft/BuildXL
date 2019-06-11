// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import { NetFx } from "Sdk.BuildXL";
import * as Managed from "Sdk.Managed";
import * as BuildXLSdk from "Sdk.BuildXL";

namespace SandboxedProcessExecutor {

    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    @@public
    export const exe = BuildXLSdk.executable({
        generateLogs: true,
        allowUnsafeBlocks: true,
        assemblyName: "SandboxedProcessExecutor",
        rootNamespace: "BuildXL.SandboxedProcessExecutor",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Interop.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.dll,
            importFrom("BuildXL.Engine").Processes.dll,
        ],
    });
}
