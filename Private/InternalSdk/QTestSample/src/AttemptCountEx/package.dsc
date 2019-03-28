// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as Managed from "Sdk.Managed";
import {Artifact} from "Sdk.Transformers";
import * as VsTest from "VisualStudio.UnitTest";
import * as HelloWorld from "HelloWorld";
import * as QTest from "Sdk.QTest";

// Example script that shows how to invoke QTests
// This unit test verifies that necessary test data exists in the test sandbox
// This example shows how to explicitly list all the files that need to be copied to sandbox
const testDll = Managed.library({
    assemblyName: "AttemptCountEx",
    sources: [f`UnitTests.cs`],
    references: [
        Managed.StandardAssemblies.MsCorLib.dll,
        VsTest.Microsoft.VisualStudio.QualityTools.UnitTestFramework.dll,
        HelloWorld.exe
    ],

    skipDocumentationGeneration: true
});

export const qTestRun = QTest.runQTest({
    testAssembly: Artifact.input(testDll.dll),
    qTestInputs: [
        testDll.dll,
        testDll.pdb,
        VsTest.Microsoft.VisualStudio.QualityTools.UnitTestFramework.dll.dll,
        HelloWorld.exe.dll,
        f`TestData/ParentData.txt`
    ],    
    qTestType: QTest.QTestType.msTest_latest,
    qTestAttemptCount: 5
});

