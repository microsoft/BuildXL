// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Deployment from "Sdk.Deployment";
import * as BuildXLSdk from "Sdk.BuildXL";

namespace Tests.Osx {
    export declare const qualifier : {configuration: "debug" | "release", targetFramework: "netcoreapp3.0", targetRuntime: "osx-x64"};

    const sharedBinFolderName = a`shared_bin`;

    const tests = createAllDefs();

    function createAllDefs() : TestDeploymentDefinition[] {
        return [

            // App
            createDef(importFrom("BuildXL.App").UnitTests.Bxl.dll, true),

            // Cache
            // DistributedTest
            createDef(importFrom("BuildXL.Cache.ContentStore").DistributedTest.dll, true,
            /* deploySeparately */ false,
            /* testClasses */ undefined,
            /* categories */ [],
            /* noCategories */ ["LongRunningTest", "Simulation", "Performance"]
        ),

            // GrpcTest
            createDef(importFrom("BuildXL.Cache.ContentStore").Test.dll, true),
            createDef(importFrom("BuildXL.Cache.ContentStore").InterfacesTest.dll, true),

            // DistributedTest
            // VstsTest
            createDef(importFrom("BuildXL.Cache.MemoizationStore").Test.dll, true),
            createDef(importFrom("BuildXL.Cache.MemoizationStore").InterfacesTest.dll, true),

            createDef(importFrom("BuildXL.Cache.Core.UnitTests").Analyzer.dll, true),
            createDef(importFrom("BuildXL.Cache.Core.UnitTests").BasicFilesystem.dll, true),
            createDef(importFrom("BuildXL.Cache.Core.UnitTests").InputListFilter.dll, true),
            createDef(importFrom("BuildXL.Cache.Core.UnitTests").Interfaces.dll, true),
            createDef(importFrom("BuildXL.Cache.Core.UnitTests").MemoizationStoreAdapter.dll, true),
            createDef(importFrom("BuildXL.Cache.Core.UnitTests").VerticalAggregator.dll, true),

            // Engine
            createDef(importFrom("BuildXL.Core.UnitTests").Cache.dll, true),
            createDef(importFrom("BuildXL.Core.UnitTests").Cache.Plugin.Core.dll, true),
            createDef(importFrom("BuildXL.Core.UnitTests").Engine.dll, true,
                /* deploySeparately */ false,
                /* testClasses */ undefined,
                /* categories */ [ "ValuePipTests", "DirectoryArtifactIncrementalBuildTests" ]
            ),
            createDef(importFrom("BuildXL.Core.UnitTests").Test.BuildXL.FingerprintStore.dll, true),
            createDef(importFrom("BuildXL.Core.UnitTests").Ide.Generator.dll, true),
            createDef(importFrom("BuildXL.Core.UnitTests").Processes.test_BuildXL_Processes_dll, true),
            createDef(importFrom("BuildXL.Core.UnitTests").Scheduler.dll, true,
                /* deploySeparately */ false,
                /* testClasses */ undefined,
                /* categories */ importFrom("BuildXL.Core.UnitTests").Scheduler.categoriesToRunInParallel
            ),
            createDef(importFrom("BuildXL.Core.UnitTests").Scheduler.IntegrationTest.dll, true,
                /* deploySeparately */ false,
                /* testClasses */ undefined,
                /* categories */ importFrom("BuildXL.Core.UnitTests").Scheduler.IntegrationTest.categoriesToRunInParallel
            ),

            // Frontend
            createDef(importFrom("BuildXL.FrontEnd.SdkTesting").TestGenerator.dll, true),
            createDef(importFrom("BuildXL.FrontEnd").TypeScript.Net.UnitTests.testDll, true, /* deploySeparately */ true),
            createDef(importFrom("BuildXL.FrontEndUnitTests").Core.dll, true),
            createDef(importFrom("BuildXL.FrontEndUnitTests").Download.dll, true),
            createDef(importFrom("BuildXL.FrontEndUnitTests").Nuget.dll, true),
            createDef(importFrom("BuildXL.FrontEndUnitTests").Script.Ambients.dll, true),
            createDef(importFrom("BuildXL.FrontEndUnitTests").Script.dll, true,
                /* deploySeparately */ false,
                /* testClasses */ undefined,
                /* categories */ importFrom("BuildXL.FrontEndUnitTests").Script.categoriesToRunInParallel
            ),
            createDef(importFrom("BuildXL.FrontEndUnitTests").Script.Debugger.dll, true),
            createDef(importFrom("BuildXL.FrontEndUnitTests").Script.ErrorHandling.dll, true),
            createDef(importFrom("BuildXL.FrontEndUnitTests").Script.Interpretation.dll, true),
            createDef(importFrom("BuildXL.FrontEndUnitTests").Script.PrettyPrinter.dll, true),
            createDef(importFrom("BuildXL.FrontEndUnitTests").Script.V2Tests.dll, true),
            createDef(importFrom("BuildXL.FrontEndUnitTests").Workspaces.dll, true),
            createDef(importFrom("BuildXL.FrontEndUnitTests").FrontEnd.Sdk.dll, true),

            // Ide
            createDef(importFrom("BuildXL.Ide").LanguageService.Server.test, true),

            // Pips
            createDef(importFrom("BuildXL.Pips.UnitTests").Core.dll, true),

            // Tools
            createDef(importFrom("BuildXL.Tools.UnitTests").Test.Tool.Analyzers.dll, true),
            createDef(importFrom("BuildXL.Tools.UnitTests").Test.BxlScriptAnalyzer.dll, true),
            createDef(importFrom("BuildXL.Tools.UnitTests").Test.Tool.SandboxExec.dll, true),

            // Utilities
            createDef(importFrom("BuildXL.Utilities.Instrumentation.UnitTests").Core.dll, true),
            createDef(importFrom("BuildXL.Utilities.UnitTests").Collections.dll, true),
            createDef(importFrom("BuildXL.Utilities.UnitTests").Configuration.dll, true),
            createDef(importFrom("BuildXL.Utilities.UnitTests").Ipc.dll, true),
            createDef(importFrom("BuildXL.Utilities.UnitTests").KeyValueStoreTests.dll, true),
            createDef(importFrom("BuildXL.Utilities.UnitTests").Storage.dll, true),
            createDef(importFrom("BuildXL.Utilities.UnitTests").Storage.Untracked.dll, true),
            createDef(importFrom("BuildXL.Utilities.UnitTests").ToolSupport.dll, true),
            createDef(importFrom("BuildXL.Utilities.UnitTests").Core.dll, true),
        ];
    }

    interface TestDeploymentDefinition extends Deployment.NestedDefinition {
        assembly: File;
        subfolder: PathAtom;
        enabledForMac: boolean;
        testClasses: string[];
        categoriesToRunInParallel: string[];
        categoriesToNeverRun: string[];
        runSuppliedCategoriesOnly: boolean;
    }

    function createDef(testResult: BuildXLSdk.TestResult, enabled: boolean, deploySeparately?: boolean,
            testClasses?: string[], categories?: string[], noCategories?: string[], runSuppliedCategoriesOnly?: boolean) : TestDeploymentDefinition {
        let assembly = testResult.testDeployment.primaryFile;
        return <TestDeploymentDefinition>{
            subfolder: deploySeparately
                ? assembly.nameWithoutExtension // deploying separately because some files clash with shared_bin deployment
                : sharedBinFolderName,          // deploying into shared_bin (together with 'coreDrop' BuildXL binaries) to save space for test deployment
            contents: [
                testResult.testDeployment.deployedDefinition
            ],

            assembly: assembly,
            testAssemblies: [],
            enabledForMac: enabled,
            testClasses: testClasses,
            categoriesToRunInParallel: categories,
            categoriesToNeverRun: noCategories,
            runSuppliedCategoriesOnly: runSuppliedCategoriesOnly || false
        };
    }

    function genXUnitExtraArgs(definition: TestDeploymentDefinition): string {
        return (definition.testClasses || [])
            .map(testClass => `-class ${testClass}`)
            .join(" ");
    }

    function quoteString(str: string): string {
        return '"' + str.replace('"', '\\"') + '"';
    }

    function generateStringArrayProperty(propertyName: string, array: string[], indent: string): string[] {
        return generateArrayProperty(propertyName, (array || []).map(quoteString), indent);
    }

    function generateArrayProperty(propertyName: string, literals: string[], indent: string): string[] {
        if ((literals || []).length === 0) {
            return [];
        }

        return [
            `${indent}${propertyName}: [`,
            ...literals.map(str => `${indent}${indent}${str},`),
            `${indent}]`
        ];
    }

    function generateDsVarForTest(def: TestDeploymentDefinition): string {
        const varName = def.assembly.name.toString().replace(".", "_");
        const indent = "    ";
        return [
            `@@public export const xunit_${varName} = runXunit({`,
            `    testAssembly: f\`${def.subfolder}/${def.assembly.name}\`,`,
            `    runSuppliedCategoriesOnly: ${def.runSuppliedCategoriesOnly},`,
            ...generateStringArrayProperty("classes", def.testClasses, indent),
            ...generateStringArrayProperty("categories", def.categoriesToRunInParallel, indent),
            ...generateStringArrayProperty("noCategories", def.categoriesToNeverRun, indent),
            `});`,
            ``
        ].join("\n");
    }

    function createSpecFile(definitions: TestDeploymentDefinition[]): string {
        return tests
            .filter(def => def.enabledForMac)
            .map(generateDsVarForTest)
            .join("\n");
    }

    function createUnixTestRunnerScript(definitions: TestDeploymentDefinition[]): string {
        const runTestCommands = tests
            .filter(def => def.enabledForMac)
            .map(def => `run_xunit TestProj/tests/${def.subfolder}${' '}${def.assembly.name}${' '}${genXUnitExtraArgs(def)}`);

        return [
            "#!/bin/bash",
            "",
            "MY_DIR=$(cd `dirname ${BASH_SOURCE[0]}` && pwd)",
            "source $MY_DIR/xunit_runner.sh",
            "",
            "numTestFailures=0",
            "trap \"((numTestFailures++))\" ERR",
            "",
            ...runTestCommands,
            "",
            "exit $numTestFailures"
        ].join("\n");
    }

    function renderFileLiteral(file: string): string {
        if (file === undefined) return "undefined";
        return "f`" + file + "`";
    }

    function createModuleConfig(projectFiles?: string[]): string {
        return [
            'module({',
            '    name: "BuildXLXUnitTests",',
            '    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,',
            ...generateArrayProperty("projects", (projectFiles || []).map(renderFileLiteral), "    "),
            '});'
        ].join("\n");
    }

    function writeFile(fileName: PathAtom, content: string): DerivedFile {
        return Transformer.writeAllText({
            outputPath: p`${Context.getNewOutputDirectory("mac-tests")}/${fileName}`,
            text: content
        });
    }

    /*
        These deployments we only use to run tests on Mac, which must stay true because they contain Mac-specific native binaries.

        Folder layout:

            ├── [TestProj]
            │   ├── [sdk]
            │   │   ├── [dotnet]
            |   │   │   └── ...
            │   │   └── [xunit]
            |   │       └── ...
            │   ├── [tests]
            │   │   ├── [shared_bin]
            |   │   │   └── ... (BuildXL core drop + most of the test deployments)
            │   │   ├── [Test.TypeScript.Net]
            |   │   │   └── ... (only Test.TypeScript.Net is deployed separately because of some file clashes)
            │   │   ├── helpers.dsc
            │   │   ├── main.dsc
            │   │   └── module.config.dsc
            │   └── config.dsc
            ├── bash_runner.sh
            ├── env.sh
            ├── test_runner.sh
            └── xunit_runner.sh
    */
    export const deployment : Deployment.Definition = {
        contents: [
            f`xunit_runner.sh`,
            f`test_runner.sh`,
            writeFile(a`bash_runner.sh`, createUnixTestRunnerScript(tests)),
            f`${Context.getMount("Sandbox").path}/MacOs/scripts/env.sh`,
            {
                subfolder: a`TestProj`,
                contents: [
                    Deployment.createFromDisk(d`TestProj`),
                    {
                        subfolder: r`tests`,
                        contents: [
                            // deploying BuildXL core drop (needed to execute tests)
                            {
                                subfolder: sharedBinFolderName,
                                contents: [ BuildXL.deployment ]
                            },
                            // some of these tests may also get deployed into `sharedBinFolderName` folder to save space
                            ...tests,
                            // other generated DScript specs
                            writeFile(a`main.dsc`, createSpecFile(tests)),
                            writeFile(a`module.config.dsc`, createModuleConfig([ `helpers.dsc`, `main.dsc`]))
                        ]
                    },
                    {
                        subfolder: r`sdk`,
                        contents: [
                            Deployment.createFromDisk(d`${Context.getMount("SdkRoot").path}/DotNetCore`)
                        ]
                    }
                ]
            }
        ]};
}
