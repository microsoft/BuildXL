// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import { NetFx } from "Sdk.BuildXL";
import * as Managed from "Sdk.Managed";
import * as BuildXLSdk from "Sdk.BuildXL";

namespace AdoBuildRunner {

    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "AdoBuildRunner",
        sources: [
            ...globR(d`./Build/`, "*.cs"),
            ...globR(d`./Vsts/`, "*.cs"),
            f`Constants.cs`,
            f`Program.cs`,
        ],
        references: [
            ...importFrom("BuildXL.Utilities").Native.securityDlls,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("Microsoft.AspNet.WebApi.Client").pkg,
            importFrom("Microsoft.TeamFoundationServer.Client").pkg,
            importFrom("Microsoft.VisualStudio.Services.Client").pkg,
            importFrom("Microsoft.TeamFoundation.DistributedTask.WebApi").pkg,
            importFrom("Microsoft.TeamFoundation.DistributedTask.Common.Contracts").pkg,
        ],
    });
}
