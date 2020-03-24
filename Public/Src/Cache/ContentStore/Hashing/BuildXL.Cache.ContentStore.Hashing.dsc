// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as ILRepack from "Sdk.Managed.Tools.ILRepack";
import * as Shared from "Sdk.Managed.Shared";

namespace Hashing {
    export declare const qualifier : BuildXLSdk.DefaultQualifierWithNetStandard20;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.ContentStore.Hashing",
        sources: globR(d`.`, "*.cs"),
        references: [
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
        runtimeContent: Context.getCurrentHost().os !== "win" ? [] : [
            {
                subfolder: "x64",
                contents: [
                    BuildXLSdk.Factory.createBinary(importFrom("DeduplicationSigned").pkg.contents, r`build/net45/x64/ddpchunk.dll`),
                    BuildXLSdk.Factory.createBinary(importFrom("DeduplicationSigned").pkg.contents, r`build/net45/x64/ddptrace.dll`)
                ]
            },
        ],
        allowUnsafeBlocks: true,
    });
}
