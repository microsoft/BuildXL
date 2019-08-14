// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer}   from "Sdk.Transformers";
import * as Deployment from "Sdk.Deployment";

namespace StandaloneTestSupport {
    export declare const qualifier: {};
    export const deploymentTargetPath = r`tests/standaloneTest`;

    function createToolModule(moduleName: string, toolDirectory: StaticDirectory) : Deployment.NestedDefinition {
        const specContent = [
            'import {Transformer} from \"Sdk.Transformers\";',
            '',
            '@@public',
            'export const pkg: NugetPackage = {contents: Transformer.sealSourceDirectory(d`.`, Transformer.SealSourceDirectoryOption.allDirectories), dependencies: []};',
        ].join("\n");
        const moduleContent = StandaloneTestUtils.createModuleConfig(moduleName, ["main.dsc"]);
        return {
            subfolder: toolDirectory.root.name,
            contents: [
                StandaloneTestUtils.writeFile(a`main.dsc`, specContent),
                StandaloneTestUtils.writeFile(a`module.config.dsc`, moduleContent),
                toolDirectory,
            ]
        };
    }

    const configContent = [
        'config({',
        '   modules: [',
        '       d`sdk`,',
        '       d`tools`,',
        '       d`unittests`,',
        '   ].mapMany(dir => [\"module.config.dsc\", \"package.config.dsc\"].mapMany(moduleConfigFileName => globR(dir, moduleConfigFileName)))',
        '});',
    ].join("\n");

    /**
     * Layout:
     * tests/standaloneTest
     *     sdk/
     *         transformers/
     *         StandaloneTestSDK/
     *     tools/
     *         xunit.runner.console/
     *         NETCoreSDK/
     *     unittests/ -- see StandaloneTest.dsc
     *     config.dsc
     *     RunUnitTests.cmd
     *     RunExecutableTests.cmd
     */
    const deployment: Deployment.Definition = {
            contents: [
            {
                subfolder: a`tools`,
                contents: [
                    createToolModule("xunit.runner.console", importFrom("xunit.runner.console").Contents.all),
                    createToolModule("NETCoreSDK", importFrom("DotNet-Runtime.win-x64").extracted),
                ]
            },
            {
                subfolder: a`sdk`,
                contents: [
                    {
                        subfolder: a`transformers`,
                        contents: [
                            Deployment.createFromDisk(d`${Context.getMount("SdkRoot").path}/Transformers`),
                        ]
                    },
                    {
                        subfolder: a`StandaloneTestSDK`,
                        contents: [
                            { file: f`./StandaloneTestSDK/main.dsc.literal`, targetFileName: a`main.dsc` },
                            { file: f`./StandaloneTestSDK/module.config.dsc.literal`, targetFileName: a`module.config.dsc` },
                            f`${Context.getMount("SdkRoot").path}/Managed/Testing/XUnit/xunitarguments.dsc`,
                        ]
                    }
                ]
            },
            StandaloneTestUtils.writeFile(a`config.dsc`, configContent),
            f`./RunUnitTests.cmd`,
            f`./RunExecutableTests.cmd`,
        ]
    };

    function deploy(definition: Deployment.Definition, targetLocation: RelativePath) {
        if (!StandaloneTestUtils.shouldDeployStandaloneTest) return undefined;
        return DeploymentHelpers.deploy({
            definition: definition, 
            targetLocation: targetLocation});
    }

    @@public
    export const unitTestSupportDeployed = deploy(deployment, deploymentTargetPath);
}