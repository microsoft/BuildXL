// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Native from "Sdk.Native";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as BoostTest from "Sdk.Native.Tools.BoostTest";

export declare const qualifier: BuildXLSdk.DefaultQualifier;

// Unit tests run in x64 only
const testx64 = UnitTests.withQualifier({platform: "x64", configuration : qualifier.configuration}).test;

namespace UnitTests {
    export declare const qualifier: BuildXLSdk.PlatformDependentQualifier;

    export const test = Context.getCurrentHost().os === "win" && BoostTest.test({
        outputFileName: PathAtom.create("DetoursUnitTests.exe"),
        sources: globR(d`.`, "*.cpp"),
        includes: [
            ...globR(d`.`, "*.h"),
            importFrom("WindowsSdk").UM.include,
            importFrom("WindowsSdk").Shared.include,
            importFrom("WindowsSdk").Ucrt.include,
            importFrom("VisualCpp").include,
            importFrom("BuildXL.Sandbox.Windows").Core.includes,
            importFrom("BuildXL.Sandbox.Windows").Detours.Include.includes,
        ],
        libraries: [
            ...importFrom("WindowsSdk").UM.standardLibs,
            importFrom("VisualCpp").lib,
            importFrom("WindowsSdk").Ucrt.lib,
            importFrom("BuildXL.Sandbox.Windows").Core.testDll.importLibrary,
        ],
        runtimeContent: [
            importFrom("BuildXL.Sandbox.Windows").Core.testDll.binaryFile]
        });
}