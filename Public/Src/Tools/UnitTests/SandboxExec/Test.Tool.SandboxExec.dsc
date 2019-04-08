// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as Deployment from "Sdk.Deployment";

namespace Test.Tool.SandboxExec {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.Tool.SandboxExec",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Tools").SandboxExec.exe,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll
        ],
    });
}
