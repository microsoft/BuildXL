// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";
import * as Managed from "Sdk.Managed";
import { NetFx } from "Sdk.BuildXL";
import {Transformer} from "Sdk.Transformers";

namespace MsBuild {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.FrontEnd.MsBuild",
        generateLogs: true,
        sources: globR(d`.`, "*.cs"),
        references: [
            ...BuildXLSdk.tplPackages,
            importFrom("BuildXL.Engine").Cache.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("Newtonsoft.Json").pkg,
            BuildXL.FrontEnd.Utilities.dll,
            TypeScript.Net.dll,
            Script.dll,
            Core.dll,
            Serialization.dll,
            Sdk.dll,
        ],
        internalsVisibleTo: [
            "Test.BuildXL.FrontEnd.MsBuild",
        ],
        runtimeContent: [
            // CODESYNC: \Public\Src\IDE\VsCode\BuildXL.IDE.VsCode.dsc 
            // We exclude the VbCsCompiler from the VsCode extension to save space.
            {
                subfolder: r`tools/vbcslogger/net472`,
                contents: [importFrom("BuildXL.Tools").VBCSCompilerLogger
                    .withQualifier({ targetFramework: "net472" }).dll]
            },
            {
                subfolder: r`tools/vbcslogger/dotnetcore`,
                contents: [importFrom("BuildXL.Tools").VBCSCompilerLogger
                    .withQualifier({ targetFramework: "netcoreapp3.1" }).dll]
            },
            {
                subfolder: r`tools`,
                contents: [importFrom("BuildXL.Tools").MsBuildGraphBuilder.deployment],
            }
        ]
    });
}
