// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";
import * as Deployment from "Sdk.Deployment";
import * as Managed from "Sdk.Managed";

export const xunitConsolePackage = importFrom("xunit.runner.console").Contents.all;

// This package is published by Dotnet Arcade and contains some important fixes we need, when
// running on .NETCoreApp 3.0, see: https://github.com/dotnet/arcade/tree/master/src/Microsoft.DotNet.XUnitConsoleRunner
export const xunitNetCoreConsolePackage = importFrom("microsoft.dotnet.xunitconsolerunner").Contents.all;

/**
 * Evaluate (i.e. schedule) xUnit test runner invocation with specified arguments.
 */
@@public
export function runConsoleTest(args: TestRunArguments): Result {
    if (args.parallelGroups && args.parallelGroups.length > 0) {

        if (args.limitGroups) {
            Contract.fail("XUnit runner does not support combining parallel runs with restricting or skipping test groups");
        }

        return runMultipleConsoleTests(args);
    }

    let testDeployment = args.testDeployment;

    const tool : Transformer.ToolDefinition = Managed.Factory.createTool({
        exe: qualifier.targetFramework === "netcoreapp3.0"
            ? testDeployment.contents.getFile(r`xunit.console.dll`)
            // Using xunit executable from different folders depending on the target framework.
            // This allow us to actually to run tests targeting different frameworks.
            : xunitConsolePackage.getFile( r`tools/${qualifier.targetFramework}/xunit.console.exe`),
        runtimeDirectoryDependencies: [
            xunitConsolePackage,
        ],
    });

    const testMethod = args.method || Environment.getStringValue("[UnitTest]Filter.testMethod");
    const testClass  = args.className || Environment.getStringValue("[UnitTest]Filter.testClass");

    let arguments : Argument[] = CreateCommandLineArgument(testDeployment.primaryFile, args, testClass, testMethod);

    let execArguments : Transformer.ExecuteArguments = {
        tool: args.tool || tool,
        tags: args.tags,
        arguments: arguments,
        dependencies: [
            testDeployment.contents,
        ],
        warningRegex: "^(?=a)b", // This is a never matching warning regex. StartOfLine followed by the next char must be 'a' (look ahead), and the next char must be a 'b'.
        workingDirectory: testDeployment.contents.root,
        retryExitCodes: Environment.getFlag("RetryXunitTests") ? [1] : [],
        unsafe: args.untrackTestDirectory ? {untrackedScopes: [testDeployment.contents.root]} : undefined,
        privilegeLevel: args.privilegeLevel,
    };

    if (qualifier.targetFramework === "netcoreapp3.0") {
        execArguments = importFrom("Sdk.Managed.Frameworks").Helpers.wrapInDotNetExeForCurrentOs(execArguments);
    }

    execArguments = Managed.TestHelpers.applyTestRunExecutionArgs(execArguments, args);

    const result = Transformer.execute(execArguments);

    return {
        xmlFile:   args.xmlFile && result.getOutputFile(args.xmlFile),
        xmlV1File: args.xmlV1File && result.getOutputFile(args.xmlV1File),
        nunitFile: args.nunitFile && result.getOutputFile(args.nunitFile),
        htmlFile:  args.htmlFile && result.getOutputFile(args.htmlFile),
    };
}


function renameOutputFile(name: string, file: Path) : Path {
    return file && file.changeExtension(a`${name}.${file.extension}`);
}

function runMultipleConsoleTests(args: TestRunArguments) : Result
{
    // Run all tests with the selected traits
    for (let testGroup of args.parallelGroups)
    {
        runConsoleTest(args.override({
            // disable breaking down in groups again
            parallelGroups: undefined,

            // Avoid double-writes
            xmlFile: renameOutputFile(testGroup, args.xmlFile),
            xmlV1File: renameOutputFile(testGroup, args.xmlV1File),
            xmlFnunitFileile: renameOutputFile(testGroup, args.nunitFile),
            htmlFile: renameOutputFile(testGroup, args.htmlFile),

            traits: [
                {name: "Category", value: testGroup}
            ]
        }));
    }

    // Do a single last one that passes notraits so we will run all tests without a trait.
    return runConsoleTest(args.override({
            parallelGroups: undefined,
            noTraits: args.parallelGroups.map(testGroup => <NameValuePair>{name: "Category", value: testGroup}).concat(args.noTraits || [])
        }
    ));
}

/**
 * Command line arguments that are required for running xunit.console.
 */
@@public
export interface TestRunArguments extends ConsoleArguments, Managed.TestRunArguments, Transformer.RunnerArguments {
}