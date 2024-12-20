// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";
import * as XUnit from "Sdk.Managed.Testing.XUnit";

export declare const qualifier: BuildXLSdk.DefaultQualifier;

namespace Test.BuildXL.AdoBuildRunner {
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.Tool.AdoBuildRunner",
        sources: globR(d`.`, "*.cs"),
        testFramework: XUnit.framework,
        references: [
            importFrom("BuildXL.AdoBuildRunner").BuildXL.AdoBuildRunner.exe,

            importFrom("Newtonsoft.Json").pkg,
            importFrom("Microsoft.AspNet.WebApi.Client").pkg,
            importFrom("Microsoft.TeamFoundationServer.Client").pkg,
            importFrom("Microsoft.VisualStudio.Services.Client").pkg,
            importFrom("Microsoft.TeamFoundation.DistributedTask.WebApi").pkg,
            importFrom("Microsoft.TeamFoundation.DistributedTask.Common.Contracts").pkg,
            importFrom("System.CommandLine").pkg,
            // Note: don't add internal dependencies here
            // We want this project to be separated from BuildXL for future portability
        ],
        nullable: true
    });
}