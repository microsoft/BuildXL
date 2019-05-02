// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

namespace Interfaces {

    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.Interfaces",
        sources: globR(d`.`, "*.cs"),
        references: [
            ...addIfLazy(BuildXLSdk.isFullFramework, () => [
                NetFx.System.Dynamic.Runtime.dll,
            ]),
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Collections.dll,

            ...addIf(BuildXLSdk.isFullFramework,
                importFrom("Newtonsoft.Json").pkg
            ),

            ...addIf(BuildXLSdk.isDotNetCoreBuild,
                Managed.Factory.createBinary(importFrom("Newtonsoft.Json").Contents.all, r`lib/netstandard2.0/Newtonsoft.Json.dll`)
            ),
        ],
    });
}
