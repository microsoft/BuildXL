// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

import * as Managed      from "Sdk.Managed";
import * as Deployment   from "Sdk.Deployment";

export declare const qualifier : Managed.TargetFrameworks.All;

@@public
export const framework : Managed.TestFramework = {
    compileArguments: processArguments,
    additionalRuntimeContent: additionalRuntimeContent,
    runTest: runTest,
    name: "XUnit",
};

@@public
export const xunitReferences : Managed.Reference[] = qualifier.targetFramework === "netcoreapp3.0"
    ? [
        importFrom("xunit.assert").pkg,
        importFrom("xunit.abstractions").pkg,
        importFrom("xunit.runner.reporters").pkg,
        importFrom("xunit.extensibility.core").pkg,
        importFrom("xunit.extensibility.execution").pkg
    ]
    : [
        Managed.Factory.createBinary(importFrom("xunit.assert").Contents.all, r`lib/netstandard1.1/xunit.assert.dll`),
        Managed.Factory.createBinary(importFrom("xunit.abstractions").Contents.all, r`lib/netstandard2.0/xunit.abstractions.dll`),
        Managed.Factory.createBinary(importFrom("xunit.extensibility.core").Contents.all, r`lib/netstandard1.1/xunit.core.dll`),
        Managed.Factory.createBinary(importFrom("xunit.extensibility.execution").Contents.all, r`lib/net452/xunit.execution.desktop.dll`),
        Managed.Factory.createBinary(importFrom("xunit.runner.utility").Contents.all, r`lib/net452/xunit.runner.utility.net452.dll`)
    ];

function processArguments(args: Managed.TestArguments): Managed.TestArguments {
    return Object.merge<Managed.TestArguments>(
        {
            references: xunitReferences,
        },
        args);
}

const netStandardFramework = importFrom("Sdk.Managed.Frameworks.NetCoreApp3.0").withQualifier({targetFramework: "netcoreapp3.0"}).framework;
const xunitNetStandardRuntimeConfigFiles: File[] = Managed.RuntimeConfigFiles.createFiles(
    netStandardFramework,
    "xunit.console",
    Managed.Factory.createBinary(xunitNetCoreConsolePackage, r`/lib/netcoreapp2.0/xunit.console.dll`),
    xunitReferences,
    undefined, // appconfig
    true);

// For the DotNetCore run we need to copy a bunch more files:
function additionalRuntimeContent(args: Managed.TestArguments) : Deployment.DeployableItem[] {
    return qualifier.targetFramework !== "netcoreapp3.0" ? [] : [
        // Unfortunately xUnit console runner comes as a precompiled assembly for .NET Core, we could either go and pacakge it
        // into a self-contained deployment or treat it as a framework-dependent deployment as intended, let's do the latter
        ...(args.framework === netStandardFramework
            ? xunitNetStandardRuntimeConfigFiles
            : Managed.RuntimeConfigFiles.createFiles(
                args.framework,
                "xunit.console",
                Managed.Factory.createBinary(xunitNetCoreConsolePackage, r`/lib/netcoreapp2.0/xunit.console.dll`),
                xunitReferences,
                undefined, // appConfig
                true)),
        xunitConsolePackage.getFile(r`/tools/netcoreapp2.0/xunit.runner.utility.netcoreapp10.dll`),
        xunitNetCoreConsolePackage.getFile(r`/lib/netcoreapp2.0/xunit.console.dll`),
    ];
}

function runTest(args : TestRunArguments) : File[] {
        // Creating output files
        let logFolder = Context.getNewOutputDirectory('xunit-logs');
        let xmlResultFile = p`${logFolder}/xunit.results.xml`;

        args = Object.merge<TestRunArguments>({
            xmlFile: xmlResultFile,
            parallel: "none",
            noShadow: true,
            useAppDomains: false,
            traits: args.limitGroups && args.limitGroups.map(testGroup => <NameValuePair>{name: "Category", value: testGroup}),
            noTraits: args.skipGroups && args.skipGroups.map(testGroup => <NameValuePair>{name: "Category", value: testGroup}),
            tags: ["test", "telemetry:xUnit"]
        }, args);

        let testResult = runConsoleTest(args);
    return [
        testResult.xmlFile
    ];
}

