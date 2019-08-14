// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

import * as Managed      from "Sdk.Managed";
import * as Qtest        from "BuildXL.Tools.QTest";

export declare const qualifier : Managed.TargetFrameworks.All;
const qTestContents = importFrom("CB.QTest").Contents.all;

@@public
export const qTestTool: Transformer.ToolDefinition = {
    exe: qTestContents.getFile(r`tools/DBS.QTest.exe`),
    description: "SelfHost Dev Build QTest",
    runtimeDirectoryDependencies: [
        qTestContents
    ],
    untrackedDirectoryScopes: addIfLazy(Context.getCurrentHost().os === "win", () => [
        d`${Context.getMount("ProgramFiles").path}`,
        d`${Context.getMount("ProgramFilesX86").path}`,
        d`${Context.getMount("ProgramData").path}`,
    ]),
    dependsOnWindowsDirectories: true,
    dependsOnAppDataDirectory: true,
    prepareTempDirectory: true,
};

@@public
export interface TestRunArguments extends Managed.TestRunArguments, Qtest.QTestArguments {
};

@@public
export function getFramework(frameworkToWrap: Managed.TestFramework) : Managed.TestFramework {
    return {
        compileArguments: frameworkToWrap.compileArguments,
        additionalRuntimeContent: (args) => [ 
            ...(frameworkToWrap.additionalRuntimeContent ? frameworkToWrap.additionalRuntimeContent(args) : []), 
            f`xunit.runner.json` 
        ],
        runTest: runTest,
        name: `QTest.${frameworkToWrap.name}`,
    };
};

function runMultipleQTests(args: TestRunArguments) : File[]
{
    const resultsFromParallelGroups = args.parallelGroups.mapMany(testGroup => runTest(args.override<Qtest.QTestArguments>({
        // disable breaking down in groups again
        parallelGroups: undefined,
        limitGroups: [testGroup]
    })));
    
    const resultsFromRest = runTest(args.override<Qtest.QTestArguments>({
        parallelGroups: undefined,
        skipGroups: args.parallelGroups
    }));
    
    return [
        ...resultsFromParallelGroups,
        ...resultsFromRest
    ];
}

function runTest(args : TestRunArguments) : File[] {  
    const testMethod = Environment.getStringValue("[UnitTest]Filter.testMethod");
    const testClass  = Environment.getStringValue("[UnitTest]Filter.testClass");
     
    let additionalOptions = undefined;
    let filterArgs = [];
    let rootTestAdapterPath = importFrom("xunit.runner.visualstudio").Contents.all;
    let testAdapterPath =  d`${rootTestAdapterPath}/build/_common`;
    
    // when testmethod or testclass ignore limitGroups and skipGroups arguments
    if (testMethod || testClass) {
        // vstest doesn't support a class filter for xunit runner. 
        // reuse the FullyQualifiedName to filter the testClass, which is not an exactly match. 
        // if a class's FullyQualifiedName contains another class's FullyQualifiedName, both classes' test cases will be run.
        // user can add "." at the end of the testclass to get the exact match.
        let methodAndClassfilterArgs = [
            ...(testMethod ? ["FullyQualifiedName="+testMethod,] : []),
            ...(testClass ? ["FullyQualifiedName="+testClass,] : []),
        ];
        filterArgs = [methodAndClassfilterArgs.join("|")];
    } else if (args.parallelGroups && args.parallelGroups.length > 0) {

        if ((args.limitGroups && args.limitGroups.length > 0) 
            || (args.skipGroups && args.skipGroups.length > 0)) {
            Contract.fail("XUnit runner does not support combining parallel runs with restricting or skipping test groups");
        }

        return runMultipleQTests(args);
    }

    if (args.limitGroups) {
            filterArgs = filterArgs.concat(args.limitGroups.map(testGroup => "(TestCategory=" + testGroup + "|Category=" + testGroup + ")"));
    }

    if (args.skipGroups) {
            filterArgs = filterArgs.concat(args.skipGroups.map(testGroup => "(TestCategory!=" + testGroup + "&Category!=" + testGroup + ")"));
    }       

    if(filterArgs.length > 0){
        additionalOptions = `/testcaseFilter:"${filterArgs.join("&")}"`;      
    }

    // We use the log directory only when BuildXL is run in CloudBuild.
    // Although BuildXL can create a junction (or directory symlink) to the log directory, but
    // because the junction's target changes in every build, incremental scheduling can unnecesarily
    // makes the pips dirty. However, if we don't use a stable junction, then we won't get any cache
    // hit because the log directory can change in every build.
    const qtestLogDir = Environment.hasVariable("BUILDXL_IS_IN_CLOUDBUILD")
        ? d`${Context.getMount("LogsDirectory").path}/QTest/${args.testDeployment.primaryFile.name}`
        : d`${Context.getNewOutputDirectory("QTestLog")}`;

    const qtestDllLogDir = args.limitGroups ? p`${qtestLogDir}/${args.limitGroups[0]}` : p`${qtestLogDir}`;
    const logDir = d`${qtestDllLogDir}/${qualifier.configuration}/${qualifier.targetFramework}/${qualifier.targetRuntime}`;

    let result = Qtest.runQTest({
        testAssembly: args.testDeployment.primaryFile.path,
        qTestType: Qtest.QTestType.msTest_latest,
        qTestDirToDeploy: args.testDeployment.contents,
        qTestAdapterPath: Transformer.sealDirectory({
            root: testAdapterPath, 
            files: globR(testAdapterPath, "*")
        }),
        qTestDotNetFramework: Qtest.QTestDotNetFramework.framework45,
        qTestPlatform: Qtest.QTestPlatform.x64,
        qTestRetryOnFailure: true,
        qTestAttemptCount: 1,
        qTestIgnoreQTestSkip: true,
        qTestAdditionalOptions: additionalOptions,
        qTestTimeoutSec: 540,
        useVsTest150:true,
        vstestSettingsFile: f`test.runsettings`,
        qTestTool: Environment.hasVariable("QTEST_DEPLOYMENT_PATH") ? undefined : qTestTool,
        qTestLogs: logDir,
        tags: args.tags,
        weight: args.weight,
        privilegeLevel: args.privilegeLevel,
        qTestBuildType: qualifier.configuration,
        testSourceDir: Context.getMount("SourceRoot").path.getRelative(Context.getSpecFileDirectory().path),
    });

    return [
        result.console
    ];
}
