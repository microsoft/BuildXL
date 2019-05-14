// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as Managed from "Sdk.Managed";
import {Transformer}   from "Sdk.Transformers";

namespace StandaloneTest {

    const untrackedFramework = importFrom("Sdk.Managed.Testing.XUnit.UnsafeUnDetoured").framework;
    const deploymentTargetPath = r`${StandaloneTestSupport.deploymentTargetPath}/unittests/${qualifier.configuration}/${dotNetFramework}`;

    interface TestDeploymentDefinition extends Deployment.NestedDefinition {
        assembly: File;
        subfolder: PathAtom;
        parallelCategories: string[];
        limitCategories: string[];
        skipCategories: string[];
        untracked: boolean;
        untrackTestDirectory: boolean;
        testClasses: string[];
        deploymentOptions: Deployment.DeploymentOptions;
    }

    @@public
    export function createTestDeploymentDefinition(
        testDeployment: Deployment.OnDiskDeployment,
        testFramework: Managed.TestFramework,
        deploymentOptions?: Deployment.DeploymentOptions,
        subfolder?: PathAtom,
        parallelCategories?: string[],
        limitCategories?: string[],
        skipCategories?: string[],
        untrackTestDirectory?: boolean,
        testClasses?: string[]) : TestDeploymentDefinition {

        let assembly = testDeployment.primaryFile;
        let untracked = testFramework && testFramework.name.endsWith(untrackedFramework.name);

        return <TestDeploymentDefinition>{
            subfolder: subfolder || assembly.nameWithoutExtension,
            contents: [ testDeployment.deployedDefinition ],
            assembly: assembly,
            testAssemblies: [],
            parallelCategories: parallelCategories,
            limitCategories: limitCategories,
            skipCategories: skipCategories,
            untracked: untracked || false,
            untrackTestDirectory: untrackTestDirectory || false,
            testClasses: testClasses,
            deploymentOptions: deploymentOptions,
        };
    }

    function generateSpecContentForTest(def: TestDeploymentDefinition): string {
        const assemblyName = def.assembly.name.toString();
        const varName = assemblyName.replace(".", "_");
        const indent = "    ";
        return [
            '@@public',
            `export const unitTest = StandaloneTestSDK.runUnitTest({`,
            `    description: "${assemblyName} [${qualifier.configuration}, ${dotNetFramework}]",`,
            `    testAssembly: f\`${def.assembly.name}\`,`,
            `    untracked: ${def.untracked},`,
            `    wrapInDotNet: ${$.isDotNetCoreBuild},`,
            `    untrackTestDirectory: ${def.untrackTestDirectory},`,
            StandaloneTestUtils.generateArrayProperty("parallelCategories", def.parallelCategories, indent, StandaloneTestUtils.quoteString),
            StandaloneTestUtils.generateArrayProperty("limitCategories", def.limitCategories, indent, StandaloneTestUtils.quoteString),
            StandaloneTestUtils.generateArrayProperty("skipCategories", def.skipCategories, indent, StandaloneTestUtils.quoteString),
            StandaloneTestUtils.generateArrayProperty("classes", def.testClasses, indent, StandaloneTestUtils.quoteString),
            `});`,
            ``
        ].join("\n");
    }

    function createSpecFile(testDefinition: TestDeploymentDefinition): string {
        return [
            'import * as StandaloneTestSDK from \"StandaloneTestSDK\";',
            '',
            generateSpecContentForTest(testDefinition),
        ].join("\n");
    }

    /**
     * Deployment layout:
     *
     * tests/standaloneTest/unittests/[Configuration]/[Framework]
     *    [TestDlls]/
     *          ... Dlls ...
     *          runUnitTest.dsc
     *          module.config.dsc
     */
    function createDeployment(testDefinition: TestDeploymentDefinition): Deployment.Definition {
        const assemblyName = testDefinition.assembly.name.toString();
        const mainSpecName = "runUnitTest.dsc";
        return {
            contents: [
                {
                    subfolder: testDefinition.subfolder,
                    contents: [
                        StandaloneTestUtils.writeFile(a`${mainSpecName}`, createSpecFile(testDefinition)),
                        StandaloneTestUtils.writeFile(
                            a`module.config.dsc`,
                            StandaloneTestUtils.createModuleConfig(
                                `${assemblyName}__${qualifier.configuration}__${dotNetFramework}`,
                                [mainSpecName])),
                    ]
                },
                testDefinition
            ]
        };
    }

    @@public
    export function deployTestDefinition(testDefinition: TestDeploymentDefinition) {
        return DeploymentHelpers.deploy({
            definition: createDeployment(testDefinition),
            targetLocation: deploymentTargetPath,
            deploymentOptions: testDefinition.deploymentOptions,
        });
    }

    @@public
    export function deploy(
        testDeployment: Deployment.OnDiskDeployment,
        testFramework: Managed.TestFramework,
        deploymentOptions?: Deployment.DeploymentOptions,
        subfolder?: PathAtom,
        parallelCategories?: string[],
        limitCategories?: string[],
        skipCategories?: string[],
        untrackTestDirectory?: boolean,
        testClasses?: string[]) {

        if (!StandaloneTestUtils.shouldDeployStandaloneTest) return undefined;
        return deployTestDefinition(createTestDeploymentDefinition(
            testDeployment,
            testFramework,
            /* deploymentOptions:    */ deploymentOptions,
            /* subfolder:            */ subfolder,
            /* parallelCategories:   */ parallelCategories,
            /* limitCategories:      */ limitCategories,
            /* skipCategories:       */ skipCategories,
            /* untrackTestDirectory: */ untrackTestDirectory,
            /* testClasses:          */ testClasses));
    }
}