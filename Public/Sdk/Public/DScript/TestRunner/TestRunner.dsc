// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as Managed from "Sdk.Managed";
import * as Frameworks from "Sdk.Managed.Frameworks";

import {Transformer, Artifact, Cmd} from "Sdk.Transformers";

import * as BuildXLSdk from "Sdk.BuildXL";
import * as SdkTesting from "BuildXL.FrontEnd.SdkTesting";

export declare const qualifier : Managed.TargetFrameworks.All;

/**
 * Test arguments
 */
@@public
export interface TestArguments {
    /** DScript files with tests */
    testFiles: File[];

    /** Folders with SDK's to add to the resolvers of the files under test. */
    sdkFolders?: (Directory|StaticDirectory)[];

    /** Additional files that are used by the test through the testFolder */
    additionalDependencies?: File[];

    /**
     * If a test generates pips, those pips are validated against a checked in file (lkg) that describes those pips.
     * This flags controls whether those files should automatically be fixed (deleted, created, updated).
     */
    autoFixLkgs?: boolean;
}

/** Executes DScript test file by generating C#, compiling that and then running under xunit.*/
@@public
export function test(args: TestArguments): Managed.TestResult {
    const testSourcesFolder = Context.getNewOutputDirectory("testSources");
    const testBinaryFolder = Context.getNewOutputDirectory("testBinary");

    Contract.requires(
        args.testFiles.length > 0,
        "Must have at least one testFile to generate"
    );

    const sdkRoot = Context.getMount("SdkRoot").path;
    const sdksToTest = [
        d`${sdkRoot}/Prelude`,
        d`${sdkRoot}/Transformers`,
        d`${sdkRoot}/DScript/Testing`,
        ...(args.sdkFolders || [])
    ];

    const sealedSdksToTest : StaticDirectory[] = sdksToTest.map(dir =>
        isDirectory(dir)
            ? Transformer.sealSourceDirectory(dir, Transformer.SealSourceDirectoryOption.allDirectories)
            : dir
        );

    const getLkgDirForTestFile = (file : File) => d`${file.parent}/${file.nameWithoutExtension}`;
    const lkgFiles = args.testFiles.mapMany(file => glob(getLkgDirForTestFile(file), "*.lkg"));

    // 1: Generate C#
    const generatedTestSources = TestGenerator.generate({
        outputFolder: testSourcesFolder,
        testFiles: args.testFiles,
        lkgFiles: lkgFiles,
        sdksToTest: sealedSdksToTest,
    });

    // Run the test with Xunit
    const result = Managed.test({
        // TODO: QTest
        testFramework: importFrom("Sdk.Managed.Testing.XUnit").framework,
        framework: Frameworks.framework,
        assemblyName: "Testing",
        sourceFolders: [
            generatedTestSources.generatedFiles
        ],
        assemblyInfo: {
            comVisible: false
        },
        skipDocumentationGeneration: true,
        references: [
            SdkTesting.Helper.dll,
            ...importFrom("Sdk.Managed.Testing.XUnit").xunitReferences
        ],
        tools: {
            csc: {
                // Disable version mismatch warnings
                noWarnings: [1701, 1702],
            }
        },
        runTestArgs: {
            tools: {
                exec: {
                    dependencies: [
                        ...(args.testFiles || []),
                        ...lkgFiles,
                        ...(args.additionalDependencies || []),
                        ...(sealedSdksToTest || [])
                    ],
                    environmentVariables: [
                        {name: "AutoFixLkgs", value: args.autoFixLkgs ? "1" : "0"},
                    ],
                    unsafe: {
                        untrackedScopes: args.autoFixLkgs ? args.testFiles.map(getLkgDirForTestFile).unique() : []
                    }
                }
            }
        }
    });

    return result;
}

function isDirectory(item: Directory|StaticDirectory) : item is Directory {
    return typeof item === "Directory";
}

namespace TestGenerator {

    // Narrow the testGenerator tool to the supported qualifiers
    const testGeneratorContents = SdkTesting.TestGeneratorDeployment.withQualifier({
        configuration: qualifier.configuration
    }).contents;

    @@public
    export const tool: Transformer.ToolDefinition = {
        exe: testGeneratorContents.getFile(Context.getCurrentHost().os === "win" ? r`Win/TestGenerator.exe` : r`MacOs/TestGenerator`),
        dependsOnWindowsDirectories: true,
        prepareTempDirectory: true,
        runtimeDirectoryDependencies: [testGeneratorContents],
    };

    /** Arguments for TestGenerator */
    @@public
    export interface Arguments extends Transformer.RunnerArguments {

        /** The directory to generate C# files in */
        outputFolder: Directory;

        /** The DScript test files to test */
        testFiles: File[];

        /** The files used for Lkg comparison */
        lkgFiles?: File[];

        /** The sdks the testFiles can use */
        sdksToTest?: StaticDirectory[];
    }

    /** The TestGenerator result */
    @@public
    export interface Result {

        /** The sealed dynamic directory with the generated C# files */
        generatedFiles: StaticDirectory;
    }

    /** Generates C# files from testFiles */
    @@public
    export function generate(args: Arguments): Result {
        const commandLineArgs: Argument[] = [
            Cmd.startUsingResponseFile(),
            Cmd.option(
                "/outputFolder:",
                Artifact.output(args.outputFolder)
            ),
            Cmd.options(
                "/testFile:",
                Artifact.inputs(args.testFiles)
            ),
            Cmd.options(
                "/lkgFile:",
                Artifact.inputs(args.lkgFiles)
            ),
            Cmd.options(
                "/sdkToTest:",
                Artifact.inputs(args.sdksToTest)
            ),
        ];

        const result = Transformer.execute({
            tool: args.tool || tool,
            arguments: commandLineArgs,
            workingDirectory: args.outputFolder
        });

        return {
            generatedFiles: result.getOutputDirectory(args.outputFolder)
        };
    }
}
