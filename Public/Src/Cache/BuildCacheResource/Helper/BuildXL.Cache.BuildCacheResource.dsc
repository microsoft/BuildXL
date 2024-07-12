// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";

export declare const qualifier: BuildXLSdk.DefaultQualifierWithNet472AndNetStandard20;

namespace Helper {
    @@public
    export const dll =  BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.BuildCacheResource.Helper",
        sources: globR(d`.`,"*.cs"),
        references: [
            importFrom("System.Threading.Tasks.Extensions").pkg,
            ...addIfLazy(!BuildXLSdk.isDotNetCore, () => [
                importFrom("System.Text.Json").pkg,
                importFrom("System.Memory").pkg,])
        ],
        nullable: true,
    });
}