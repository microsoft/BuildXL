// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";

namespace AriaCommon {
    export declare const qualifier: BuildXLSdk.AllSupportedQualifiers;
    @@public
    export const dll = BuildXLSdk.library({
        allowUnsafeBlocks: true,
        assemblyName: "BuildXL.AriaCommon",
        sources: globR(d`.`, '*.cs'),
        nullable: true,
        skipDefaultReferences: true,
        references: [
            ...addIf(BuildXLSdk.isDotNetCore,
                importFrom("Microsoft.Applications.Events.Server").pkg),
        ],
        runtimeContent: [
            ...addIf(qualifier.targetRuntime === "win-x64",
                AriaNative.deployment
            ),
        ]
    });
}
