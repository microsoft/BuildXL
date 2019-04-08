// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as Managed from "Sdk.Managed";
import {Artifact, Cmd} from "Sdk.Transformers";
import * as VsTest from "VisualStudio.UnitTest";
import * as HelloWorld from "HelloWorld";
import * as QTest from "Sdk.QTest";

// Example script that shows how to invoke QTests
// This example uses QTestDirToDeploy where all test contents are placed into a sandbox and provided as input to QTest
// This unit test verifies that necessary test data exists in the test sandbox
const testDll = Managed.library({
    assemblyName: "QTestSample",
    sources: [f`UnitTests.cs`],
    references: [
        Managed.StandardAssemblies.MsCorLib.dll,
        VsTest.Microsoft.VisualStudio.QualityTools.UnitTestFramework.dll,
        HelloWorld.exe
    ],

    skipDocumentationGeneration: true
});

const testDataDeployment: Deployment.Definition = {
    contents: [
        {
            subfolder: PathAtom.create("TestData"),
            contents: [
                Deployment.createFromDisk(d`TestData`)
            ]
        }
    ]
};

const deployedAssembly = Managed.Sdk.deployAssembly({
    assembly: testDll,
    otherDeployments: [testDataDeployment]
});

function launchQTest(deployedAssembly: Managed.Sdk.DeployedManagedAssembly): QTest.Result {
    //Pass a new log directory to ensure that output is directly written to that directory
    const myLogsDir = Context.getNewOutputDirectory("mylogs");
    
    const qTestRun = QTest.runQTest({
        testAssembly: Artifact.input(deployedAssembly.deployedAssembly.dll),
        qTestDirToDeploy: deployedAssembly.contents,
        qTestType: QTest.QTestType.msTest_latest,
        qTestLogs: myLogsDir,
        qTestTimeoutSec: 100,
        //Helps to specify additional raw arguments to the underlying test runner
        qTestRawArgFile: f`QTestRawArgs.txt`,
        qTestIgnoreQTestSkip: true
    });

    return qTestRun;
}

export const qtestRun = launchQTest(deployedAssembly);
