// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";

@@public
export interface GvfsTestQualifer extends Qualifier
{
    configuration: "debug" | "release";
    targetFramework: "netcoreapp3.1";
    targetRuntime: "win-x64";
}

export declare const qualifier : GvfsTestQualifer;

@@public
export const dll = BuildXLSdk.library({
    assemblyName: "BuildXL.CloudTest.Gvfs",
    sources: globR(d`.`, "*.cs"), 
    skipDocumentationGeneration: true,
    references: [
        ...importFrom("Sdk.Managed.Testing.XUnit").xunitReferences,
    ],
    runtimeContent: [
        f`BuildXL.CloudTest.Gvfs.JobGroup.xml`,
    ]
});
