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

// If you want to execute the cloudtest tests from BuildXL you can do so by passing /p:[BuildXL.CloudTest]executeTests=1 to bxl.
const testFunction = Environment.getFlag("[BuildXL.CloudTest]executeTests")
    ? BuildXLSdk.test
    : BuildXLSdk.library;

@@public
export const dll = testFunction({
    assemblyName: "BuildXL.CloudTest.Gvfs",
    sources: globR(d`.`, "*.cs"), 
    skipDocumentationGeneration: true,
    references: [
        importFrom("BuildXL.Utilities").dll,
        importFrom("BuildXL.Utilities").Native.dll,
        importFrom("BuildXL.Utilities").Storage.dll,
        importFrom("BuildXL.Utilities.UnitTests").TestUtilities.XUnit.dll,

        ...importFrom("Sdk.Managed.Testing.XUnit").xunitReferences,
    ],
    runtimeContent: [
        f`BuildXL.CloudTest.Gvfs.JobGroup.xml`,
        f`setup.cmd`,
        ...importFrom("Sdk.Managed.Testing.XUnit").additionalNetCoreRuntimeContent,
    ]
});
