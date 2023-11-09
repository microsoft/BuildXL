// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import { NetFx } from "Sdk.BuildXL";
import * as Managed from "Sdk.Managed";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";

namespace AdoBuildRunner {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "AdoBuildRunner",
        sources: globR(d`.`, "*.cs"),
        references: [
            ...importFrom("BuildXL.Utilities").Native.securityDlls,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("Microsoft.AspNet.WebApi.Client").pkg,
            importFrom("Microsoft.TeamFoundationServer.Client").pkg,
            importFrom("Microsoft.VisualStudio.Services.Client").pkg,
            importFrom("Microsoft.TeamFoundation.DistributedTask.WebApi").pkg,
            importFrom("Microsoft.TeamFoundation.DistributedTask.Common.Contracts").pkg,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
        ],
    });

    @@public
    export const deployment: Deployment.Definition = {
        contents: [
            exe
        ]
    };
}
