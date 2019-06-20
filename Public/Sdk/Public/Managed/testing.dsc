// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Deployment from "Sdk.Deployment";
import * as Xml from "Sdk.Xml";

/**
 * Compiles an assembly using some of the given test frameworks defaults,
 * deploys the assembly and its closure to a testRun folder and then
 * uses the test frameworks runtest function to execute the test.
 */
@@public
export function test(args: TestArguments) : TestResult {
    let testFramework = args.testFramework;
    if (!testFramework) {
        Contract.fail("You must specify a Testing framework. For exmple: 'importFrom(\"Sdk.Managed.Testing.XUnit\").framework' ");
    }

    if (testFramework.compileArguments) {
        args = testFramework.compileArguments(args);
    }

    let assembly = library(args);

    // Deploy assemblies (with all dependencies) to special folder.
    let testDeployFolder = Context.getNewOutputDirectory("testRun");
    let additionalRuntimeContent = testFramework.additionalRuntimeContent
        ? testFramework.additionalRuntimeContent(args)
        : [];
    const testDeployment = Deployment.deployToDisk({
        definition: {
            contents: [
                assembly,
                ...additionalRuntimeContent
            ],
        },
        targetDirectory: testDeployFolder,
        primaryFile: assembly.runtime.binary.name,
        deploymentOptions: args.deploymentOptions,
        // Tag is required by ide generator to generate the right csproj file.
        tags: [ "testDeployment" ]
    });

    return assembly.merge<TestResult>(runTestOnly(
        args, 
        /* compileArguments: */ false,
        /* testDeployment:   */ testDeployment));
}

/**
 * Runs test only provided that the test deployment has been given.
 */
@@public
export function runTestOnly(args: TestArguments, compileArguments: boolean, testDeployment: Deployment.OnDiskDeployment) : TestResult
{
    let testFramework = args.testFramework;
    if (!testFramework) {
        Contract.fail("You must specify a Testing framework. For exmple: 'importFrom(\"Sdk.Managed.Testing.XUnit\").framework' ");
    }
    
    if (testFramework.compileArguments && compileArguments) {
        args = testFramework.compileArguments(args);
    }

    let testRunArgs = Object.merge(args.runTestArgs, {testDeployment: testDeployment});
    testRunArgs = prepareForTestData(testRunArgs);

    let testResults = [];

    if (!args.skipTestRun) {
        if (testRunArgs.parallelBucketCount) {
            for (let i = 0; i < testRunArgs.parallelBucketCount; i++) {
                let bucketTestRunArgs = testRunArgs.merge({
                    parallelBucketIndex: i,
                    
                });

                testResults = testResults.concat(testFramework.runTest(bucketTestRunArgs));
            }
        } else {
            testResults = testFramework.runTest(testRunArgs);
        }
    }

    return <TestResult>{
        testResults: testResults,
        testDeployment: testDeployment
    };
}

namespace TestHelpers {
    export declare const qualifier: {};

    @@public
    export const TestFilterHashIndexName = "[UnitTest]TestFilterHashIndex";
    export const TestFilterHashCountName = "[UnitTest]TestFilterHashCount";

    /** Merges test tool configuration into the given execute arguments. */
    @@public
    export function applyTestRunExecutionArgs(execArguments: Transformer.ExecuteArguments, args: TestRunArguments) : Transformer.ExecuteArguments {
        // Unit test runners often want fine control over how the process gets executed, so provide a way to override here.
        if (args.tools && args.tools.exec) {
            execArguments = args.tools.exec.merge(execArguments);
        }

        // Some unit test runners 'nest' or 'wrap' in themselves, so allow for that.
        if (args.tools && args.tools.wrapExec) {
            execArguments = args.tools.wrapExec(execArguments);
        }

        // Specify environment variables used by XUnit Assembly runner to filter tests from particular hash bucket
        // Tests are split into 'args.parallelBucketCount' buckets which are run in parallel
        if (args.parallelBucketIndex && args.parallelBucketCount)
        {
            execArguments = execArguments.merge<Transformer.ExecuteArguments>({
                environmentVariables: [
                { name: TestFilterHashIndexName, value: `${args.parallelBucketIndex}` },
                { name: TestFilterHashCountName, value: `${args.parallelBucketCount}` },
            ]
            });
        }

        return execArguments;
    }
}

function prepareForTestData(testRunArgs: TestRunArguments) : TestRunArguments
{
    const testRunData = testRunArgs.testRunData;

    if (!testRunData)
    {
        // No rundata so no need to do anything
        return testRunArgs;
    }

    const entries = testRunData
        .keys()
        .map(key => Xml.elem("Entry", 
            Xml.elem("Key", key), 
            Xml.elem("Value", ["", testRunData[key]]))
        );

    const doc = Xml.doc(
        Xml.elem("TestRunData",
            ...entries
        )
    );

    const testDataXmlFile = Xml.write(p`${Context.getNewOutputDirectory("testRunData")}/testRunData.xml`, doc);
    return testRunArgs.merge({
        tools: {
            exec: {
                // Set the environment variable so the test logic can find the data file
                environmentVariables: [
                    {
                        name: "TestRunData",
                        value: testDataXmlFile,
                    }
                ],
                // Add it to the dependencies so tests can read it.
                dependencies: [
                    testDataXmlFile,
                ]
            }
        }
    });
}

@@public
export interface TestArguments extends Arguments {
    /**
     * Which test framework to use when testing
     */
    testFramework?: TestFramework;

    /**
     * Optional special flags for the testrunner
     */
    runTestArgs?: TestRunArguments;

    /**
     * Option you can use to disable running tests
     */
    skipTestRun?: boolean;
}

@@public
export interface TestFramework {
    /** 
     * Function that allows processing of the arguments
     * that are used for compilation
     */
    compileArguments<T extends Arguments>(T) : T;

    /**
     * In case additional files need to be deployed;
     */
    additionalRuntimeContent?<T extends Arguments>(T): Deployment.DeployableItem[];


    /** The function that runs the test resulting in the test report file */
    runTest<T extends TestRunArguments>(T) : File[];

    /** Name of test framework */
    name: string;
}

@@public
export interface TestResult extends Result {
}


@@public
export interface TestRunArguments {
    /**The Deployment under tests */
    testDeployment?: Deployment.OnDiskDeployment;

    /** 
     * Test run data that is accessible from the actual test logic. 
     * This data gets written to an xml file, its path passed as an environment and is accessible using a helper
     * class in the test code.
     */
    testRunData?: Object;

    /** Nested tool options */
    tools?: {
        /** 
         * Since many test runners need custom ways to run the test processes, 
         * this is an optional settings for executing the test processs to 
         * allow for overidding the process execution settings
         * */
        exec?: {
            /** Tools dependencies. */
            dependencies?: Transformer.InputArtifact[];
            /** Tool outputs */
            outputs?: Transformer.Output[];
            /** Regex that would be used to extract errors from the output. */
            errorRegex?: string;
            /** Regex that would be used to extract warnings from the output. */
            warningRegex?: string;
            /** Environment variables. */
            environmentVariables?: Transformer.EnvironmentVariable[];
            /** Unsafe arguments */
            unsafe?: Transformer.UnsafeExecuteArguments;
            /** Mutexes to avoid running certain tests simultaneously */
            acquireMutexes?: string[];
        };

        /**
         * Some test frameworks might want to wrap other test runners
         */
        wrapExec?: (exec: Transformer.ExecuteArguments) => Transformer.ExecuteArguments;
    };
    
    /**
     * Allows running tests in various groups if the testrunner supports it
     */
    parallelGroups?: string[];

    /**
     * Allows splitting test in consistent random parallel groups
     */
    parallelBucketIndex?: number;

    /**
     * Allows splitting test in consistent random parallel groups
     */
    parallelBucketCount?: number;

    /**
     * The test groups to limit this run to.
     */
    limitGroups?: string[];

    /**
     * Allows skipping certain test groups if the testrunner supports it.
     */
    skipGroups?: string[];

    /** Untrack test directory. */ 
    untrackTestDirectory?: boolean;
    
    /**
     * Allows test runs to be tagged.
     */
    tags?: string[];

    /** Optionally override to increase the weight of test pips that require more machine resources */
    weight?: number;

    /** Privilege level required by this process to execute. */
    privilegeLevel?: "standard" | "admin";
}

@@public
export interface TestResult extends Result {
    /**
     * The test result files
     */
    testResults: File[];

    /**
     * The test deployment
     */
    testDeployment: Deployment.OnDiskDeployment;
}
