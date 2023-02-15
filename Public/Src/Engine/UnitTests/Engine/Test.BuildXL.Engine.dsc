// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as DetoursServices from "BuildXL.Sandbox.Windows";
import * as Xml from "Sdk.Xml";
import * as Deployment from "Sdk.Deployment";
import {Transformer} from "Sdk.Transformers";

namespace Engine {

    const sdkRoot = Context.getMount("SdkRoot").path;

    const libsUsedForTesting = [
        {
            subfolder: r`Sdk/Prelude`,
            contents: glob(d`${sdkRoot}/Prelude`, "*.dsc"),
        },
        {
            subfolder: r`Sdk/Transformers`,
            contents: glob(d`${sdkRoot}/Transformers`, "*.dsc"),
        },
        {
            subfolder: r`Sdk/Deployment`,
            contents: glob(d`${sdkRoot}/Deployment`, "*.dsc"),
        },
    ];

    // We generate specs as if the compilers package was source files, but point it to the downloaded NuGet
    const compilerSpecsDir = Context.getNewOutputDirectory("compilers-specs");
    const microsoftNetCompilerSpec = Transformer.writeAllText(p`${compilerSpecsDir}/module.config.bm`, 
        "module({name: 'Microsoft.Net.Compilers', version: '4.0.1', nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences});");
    const specFile = Transformer.writeAllLines(p`${compilerSpecsDir}/package.dsc`, [
        "import {Transformer} from 'Sdk.Transformers';",
        "namespace Contents {",
            "export declare const qualifier: {};",
            "@@public export const all: StaticDirectory = Transformer.sealPartialDirectory(d`package`, globR(d`package`, '*'));",
        "}"
    ]);

    // Deploy the compilers package plus the specs that refer to it
    const deployable : Deployment.Definition = {
        contents: [
            {
                subfolder: a`compilers`,
                contents: [
                    {
                         subfolder: a`package`, 
                         contents: [importFrom('Microsoft.Net.Compilers').Contents.all]
                    }, 
                    specFile, 
                    microsoftNetCompilerSpec
                ]
            }
        ]
    };

    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.Engine",
        rootNamespace: "Test.BuildXL.EngineTests",
        sources: globR(d`.`, "*.cs"),
        runTestArgs: {
            // These tests require Detours to run itself, so we won't detour the test runner process itself
            unsafeTestRunArguments: {
                runWithUntrackedDependencies: true
            },
            parallelBucketCount: 8,
            testRunData: {
                MicrosoftNetCompilersSdkLocation: "compilers/module.config.bm",
            },
            tools: {
                exec: {
                    dependencies: [
                        importFrom("Microsoft.Net.Compilers").Contents.all,
                        importFrom("Microsoft.NETCore.Compilers").Contents.all,
                    ]
                }
            }
        },    
        references: [
            EngineTestUtilities.dll,
            importFrom("BuildXL.Cache.VerticalStore").Interfaces.dll,
            importFrom("BuildXL.Cache.VerticalStore").MemoizationStoreAdapter.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Engine").Engine.dll,
            importFrom("BuildXL.Engine").Cache.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.Engine").ViewModel.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.dll,
            importFrom("BuildXL.FrontEnd").Core.dll,
            importFrom("BuildXL.FrontEnd").Script.dll,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            importFrom("BuildXL.FrontEndUnitTests").Core.dll,
        ],
        runtimeContent: [
            ...libsUsedForTesting,
            deployable
        ],
    });
}
