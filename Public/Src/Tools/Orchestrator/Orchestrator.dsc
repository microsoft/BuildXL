// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import { NetFx } from "Sdk.BuildXL";
import * as Managed from "Sdk.Managed";
import * as BuildXLSdk from "Sdk.BuildXL";

namespace Orchestrator {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "Orchestrator",
        sources: [
            ...globR(d`./Build/`, "*.cs"),
            ...globR(d`./Vsts/`, "*.cs"),
            f`Constants.cs`,
            f`Program.cs`,
        ],
        references: [
            importFrom("Newtonsoft.Json").pkg,
            importFrom("Microsoft.AspNet.WebApi.Client.NetCore").pkg,
            importFrom("Microsoft.TeamFoundationServer.Client").pkg,
            importFrom("Microsoft.VisualStudio.Services.Client").pkg,
            importFrom("Microsoft.TeamFoundation.DistributedTask.WebApi").pkg,
        ],
    });
}
