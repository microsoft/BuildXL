// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as ILRepack from "Sdk.Managed.Tools.ILRepack";
import * as Shared from "Sdk.Managed.Shared";
import * as NetCoreApp from "Sdk.Managed.Frameworks.NetCoreApp3.0";

namespace Interfaces {
    export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet451AndNetStandard20;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.ContentStore.Interfaces",
        sources: globR(d`.`, "*.cs"),
        references: [
            Hashing.dll,
            UtilitiesCore.dll,
            ...addIfLazy(BuildXLSdk.isFullFramework, () => [
                NetFx.System.Runtime.Serialization.dll,
                NetFx.System.Xml.dll,
            ]),
            ...(qualifier.targetFramework !== "netstandard2.0" ? [] :
            [
                importFrom("System.Threading.Tasks.Dataflow").pkg,
            ]),
            importFrom("System.Interactive.Async").pkg,
        ],
        allowUnsafeBlocks: true,
        internalsVisibleTo: [
            "BuildXL.Cache.ContentStore",
            "BuildXL.Cache.ContentStore.Distributed",
            "BuildXL.Cache.ContentStore.Interfaces.Test",
        ]
    });
}
