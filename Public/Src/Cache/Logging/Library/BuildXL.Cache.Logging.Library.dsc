// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
import * as SdkDeployment from "Sdk.Deployment";
import * as Managed from "Sdk.Managed.Shared";
import { NetFx } from "Sdk.BuildXL";

namespace Library {
    export declare const qualifier : BuildXLSdk.AllSupportedQualifiers;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.Logging",
        sources: globR(d`.`,"*.cs"),
        nullable: true,
        references: [
            importFrom("Microsoft.Bcl.AsyncInterfaces").pkg,
            ...importFrom("BuildXL.Cache.ContentStore").getAzureBlobStorageSdkPackages(true),
            importFrom("Microsoft.IdentityModel.Abstractions").pkg,
            importFrom("System.Memory.Data").pkg,
            importFrom("NLog").pkg,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Library.dll,
            importFrom("BuildXL.Cache.DistributedCache.Host").Configuration.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Branding.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("System.Threading.Tasks.Extensions").pkg,
            ...BuildXLSdk.systemThreadingTasksDataflowPackageReference,
            ...addIf(BuildXLSdk.isFullFramework, $.withQualifier({targetFramework:"net472"}).NetFx.System.Xml.dll),
        ],
        internalsVisibleTo: [
            "BuildXL.Cache.Logging.Test"
        ],
    });
}