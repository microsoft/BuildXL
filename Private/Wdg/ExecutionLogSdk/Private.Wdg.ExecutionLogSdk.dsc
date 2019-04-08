// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import { NetFx } from "Sdk.BuildXL";

export declare const qualifier: BuildXLSdk.FullFrameworkQualifier;

@@public
export const dll = BuildXLSdk.library({
    assemblyName: "Tool.ExecutionLogSdk",
    skipDocumentationGeneration: true,
    sources: globR(d`.`, "*.cs"),
    references: [
        NetFx.System.dll,
        importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
        importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
        importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
        importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
        importFrom("BuildXL.Pips").dll,
        importFrom("BuildXL.Engine").Cache.dll,
        importFrom("BuildXL.Engine").Engine.dll,
        importFrom("BuildXL.Engine").Processes.dll,
        importFrom("BuildXL.Engine").Scheduler.dll,
        importFrom("BuildXL.Utilities").dll,
        importFrom("BuildXL.Utilities").Collections.dll,
        importFrom("BuildXL.Utilities").Native.dll,
        importFrom("BuildXL.Utilities").Storage.dll,
        importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
    ],
});
