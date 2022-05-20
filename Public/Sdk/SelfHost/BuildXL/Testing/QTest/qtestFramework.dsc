// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";
import * as Shared       from "Sdk.Managed.Shared";
import * as Managed      from "Sdk.Managed";
import * as Qtest        from "BuildXL.Tools.QTest";

export declare const qualifier : Managed.TargetFrameworks.All;
const qTestContents = importFrom("CB.QTest").Contents.all;

const isDotNetCore = qualifier.targetFramework.startsWith("netcoreapp");

@@public
export const qTestTool: Transformer.ToolDefinition = Context.getCurrentHost().os === "win" && {
    exe: qTestContents.getFile(r`tools/DBS.QTest.exe`),
    description: "SelfHost Dev Build QTest",
    runtimeDirectoryDependencies: [
        qTestContents
    ],
    untrackedDirectoryScopes: addIfLazy(Context.getCurrentHost().os === "win", () => [
        d`${Context.getMount("ProgramFiles").path}`,
        d`${Context.getMount("ProgramFilesX86").path}`,
        d`${Context.getMount("ProgramData").path}`,
        d`${Context.getMount("AppData").path}`,
        d`${Context.getMount("LocalAppData").path}`,
        d`${Context.getMount("UserProfile").path}`,
        // To ensure that dmps are generated during crashes, QTest now includes procdmp.exe
        // However, this tool reads dbghelp.dll located in the following directory in CloudBuild machines
        d`C:/Debuggers`
    ]),
    dependsOnWindowsDirectories: true,
    dependsOnAppDataDirectory: true,
    prepareTempDirectory: true,
    timeoutInMilliseconds: Qtest.qtestDefaultTimeoutInMilliseconds
};

@@public
export interface TestRunArguments extends Managed.TestRunArguments, Qtest.QTestArguments {
};

@@public
export function getFramework(frameworkToWrap: Managed.TestFramework) : Managed.TestFramework {
    return {
        compileArguments: (args: Managed.Arguments) => {
            if (isDotNetCore) {
                args = Object.merge<Managed.Arguments>(
                    args,
                    {
                        references: [
                            importFrom("Microsoft.NET.Test.Sdk").pkg,
                            importFrom("Microsoft.TestPlatform.ObjectModel").pkg,
                            importFrom("NuGet.Frameworks").pkg
                            // importFrom("Microsoft.CodeCoverage").pkg, // TODO: NuGet spec generator fails to realize that this package does support netcoreapp1.0
                        ]
                    });
            }
            return frameworkToWrap.compileArguments(args);
        },
        additionalRuntimeContent: (args: Managed.Arguments) => [ 
            ...(frameworkToWrap.additionalRuntimeContent ? frameworkToWrap.additionalRuntimeContent(args) : []), 
            ...(isDotNetCore ? [
                // hand picking files to avoid collisions with xunit assemblies specified elsewhere
                ...importFrom("xunit.runner.visualstudio").Contents.all.getFiles([
                    r`build/netcoreapp1.0/xunit.runner.reporters.netcoreapp10.dll`,
                    r`build/netcoreapp1.0/xunit.runner.visualstudio.dotnetcore.testadapter.deps.json`,
                    r`build/netcoreapp1.0/xunit.runner.visualstudio.dotnetcore.testadapter.dll`,
                    r`build/netcoreapp1.0/xunit.runner.visualstudio.props`
                ]),
                ] : []),
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

function getQTestDotNetFramework() : Qtest.QTestDotNetFramework {
    switch (qualifier.targetFramework) {
        case "net472":
            return Qtest.QTestDotNetFramework.framework46;
        case "netstandard2.0":
        case "netcoreapp3.1":
        case "net5.0":
            return Qtest.QTestDotNetFramework.frameworkCore30;
        default:
            Contract.fail("QTest does not support " + qualifier.targetFramework);
    }
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
            ...(testClass ? ["DisplayName~"+testClass,] : []),
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
    const logDir = d`${qtestDllLogDir}/${Context.getLastActiveUseName()}/${qualifier.configuration}/${qualifier.targetFramework}/${qualifier.targetRuntime}`;
    
    let qTestRuntimeDependencies = undefined;
    let qTestEnvironmentVariables = undefined;

    // Extracting a variable, because the type checker can't analyze a dotted names properly.
    const targetFramework = qualifier.targetFramework;

    if (Shared.isDotNetCore(targetFramework)) {
        const dotNetTool = importFrom("Sdk.Managed.Frameworks").Helpers.getDotNetToolTemplate(targetFramework);
        qTestRuntimeDependencies = [
            ...dotNetTool.dependencies
        ];
        qTestEnvironmentVariables = [
            ...dotNetTool.environmentVariables,
            {name: "PATH", value: dotNetTool.tool.exe.parent}];
    }

    let result = Qtest.runQTest({
        testAssembly: args.testDeployment.primaryFile.path,
        qTestType: Qtest.QTestType.msTest_latest,
        qTestDirToDeploy: args.testDeployment.contents,
        qTestAdapterPath: Transformer.sealPartialDirectory({
            root: testAdapterPath, 
            files: rootTestAdapterPath.contents.filter(f => f.path.isWithin(testAdapterPath)),
        }),
        qTestDotNetFramework: getQTestDotNetFramework(),
        qTestPlatform: Qtest.QTestPlatform.x64,
        qTestRetryOnFailure: true,
        qTestAttemptCount: 1,
        qTestIgnoreQTestSkip: true,
        qTestAdditionalOptions: additionalOptions,
        qTestTimeoutSec: 600,
        useVsTest150: true,
        // Setting file can be passed through vstestSettingsFile or vstestSettingsFileForCoverage.
        // For BuildXL selfhost, ensure that the setting file disable parallelism. QTest by default run unit test methods in sequence,
        // but some of test adapter (our selfhost is using a test adapter) do not respect that, and must be told explicitly
        // in the setting file.
        //    <RunConfiguration>
        //        <DisableParallelization>True</DisableParallelization>
        //    </RunConfiguration>
        vstestSettingsFile: f`test.runsettings`,
        vstestSettingsFileForCoverage : f`coverage.test.runsettings`,
        qTestTool: qTestTool,
        qTestLogs: logDir,
        tags: args.tags,
        weight: args.weight,
        privilegeLevel: args.privilegeLevel,
        qTestBuildType: qualifier.configuration,
        testSourceDir: Context.getMount("SourceRoot").path.getRelative(Context.getSpecFileDirectory().path),
        qTestUnsafeArguments: args.unsafeTestRunArguments ? { 
            doNotFailForZeroTestCases: args.unsafeTestRunArguments.allowForZeroTestCases, 
            doNotTrackDependencies: args.unsafeTestRunArguments.runWithUntrackedDependencies 
        } : undefined,
        qTestRuntimeDependencies: qTestRuntimeDependencies,
        qTestEnvironmentVariables: qTestEnvironmentVariables,
        qTestAcquireSemaphores: args.tools && args.tools.exec && args.tools.exec.acquireSemaphores,
        qTestDisableCodeCoverage : args.disableCodeCoverage,
        tools: args.tools,
        qTestUntrackedScopes: args.unsafeTestRunArguments && args.unsafeTestRunArguments.untrackedScopes,
        qTestUntrackedPaths:  (
            args.unsafeTestRunArguments && 
            args.unsafeTestRunArguments.untrackedPaths && 
            args.unsafeTestRunArguments.untrackedPaths.map(path => typeof(path) === "File" 
                ? <File>path 
                : File.fromPath(args.testDeployment.contents.root.combine(<RelativePath>path)))) 
        || [],
    });

    return [
        result.console
    ];
}
