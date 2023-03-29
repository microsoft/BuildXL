// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import {Transformer} from "Sdk.Transformers";

@@public
export interface GvfsTestQualifer extends Qualifier
{
    configuration: "debug" | "release";
    targetFramework: "net6.0";
    targetRuntime: "win-x64";
}

export declare const qualifier : GvfsTestQualifer;

const gvfsPkg = importFrom("GvfsTestHelpersForBuildXL").Contents.all;

@@public
export const dll = BuildXLSdk.test({
    assemblyName: "BuildXL.CloudTest.Gvfs",
    sources: globR(d`.`, "*.cs"), 
    testFramework: importFrom("Sdk.Managed.Testing.XUnit").framework,
    runTestArgs: {
        // These tests require Detours to run itself, so we won't detour the test runner process itself
        unsafeTestRunArguments: {
            runWithUntrackedDependencies: true
        },
    },
    skipTestRun: true,
    skipDocumentationGeneration: true,
    references: [
        importFrom("BuildXL.Utilities").dll,
        importFrom("BuildXL.Utilities").Native.dll,
        importFrom("BuildXL.Utilities").Configuration.dll,
        importFrom("BuildXL.Utilities").Storage.dll,
        importFrom("BuildXL.Utilities").Utilities.Core.dll,
        importFrom("BuildXL.Utilities.UnitTests").TestUtilities.XUnit.dll,

        ...importFrom("Sdk.Managed.Testing.XUnit").xunitReferences,

        BuildXLSdk.Factory.createBinary(gvfsPkg, r`tools/GVFS.FunctionalTests.dll`),
        BuildXLSdk.Factory.createBinary(gvfsPkg, r`tools/GVFS.Tests.dll`),
        BuildXLSdk.Factory.createBinary(gvfsPkg, r`tools/nunit.framework.dll`),
    ],
    runtimeContent: [
        f`BuildXL.CloudTest.Gvfs.JobGroup.xml`,
        f`Setup.cmd`, // This is called from cloudtest JobGroup.xml file
        f`RunTests.cmd`, // This is just for local testing.
        ...importFrom("Sdk.Managed.Testing.XUnit").additionalNetCoreRuntimeContent,
        gvfsPkg,
    ],
    tools: {
        csc: {
            noWarnings: [
                // Gvfs test assemblies are not signed, ignore warning.
                8002
            ]
        },
    },
});
