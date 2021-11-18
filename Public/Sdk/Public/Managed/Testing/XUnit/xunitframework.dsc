// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

import {isDotNetCore, DotNetCoreVersion, Framework} from "Sdk.Managed.Shared";
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
export const xunitReferences : Managed.Reference[] =
    [
        importFrom("xunit.assert").pkg,
        importFrom("xunit.abstractions").pkg,
        importFrom("xunit.extensibility.core").pkg,
        importFrom("xunit.extensibility.execution").pkg
    ];

function processArguments(args: Managed.TestArguments): Managed.TestArguments {
    return Object.merge<Managed.TestArguments>(
        {
            references: [
                ...xunitReferences,
                importFrom("Microsoft.TestPlatform.TestHost").pkg,
            ],
        },
        isDotNetCore(qualifier.targetFramework)
            ? {
                deployRuntimeConfigFile: true,
                deploymentStyle: "selfContained",
            } 
            : {},
        args);
}

function getXunitConsoleRuntimeConfigNetCoreAppFiles(): File[] {
    return Managed.RuntimeConfigFiles.createFiles(
        getTargetFramework(),
        "frameworkDependent",
        "xunit.console",
        "xunit.console.dll",
        xunitReferences,
        undefined, // runtimeContentToSkip
        undefined  // nopappConfig
    );
}

function getTargetFramework(): Framework {
    switch (qualifier.targetFramework) {
        case "netcoreapp3.1": return importFrom("Sdk.Managed.Frameworks.NetCoreApp3.1").withQualifier({targetFramework: "netcoreapp3.1"}).framework;
        case "net5.0": return importFrom("Sdk.Managed.Frameworks.Net5.0").withQualifier({targetFramework: "net5.0"}).framework;
        case "net6.0": return importFrom("Sdk.Managed.Frameworks.Net6.0").withQualifier({targetFramework: "net6.0"}).framework;
        default: Contract.fail(`Unknown targetFramework version '${qualifier.targetFramework}'.`);
    } 
}

@@public
export const additionalNetCoreRuntimeContent = isDotNetCore(qualifier.targetFramework) ?
    [
        // Unfortunately xUnit console runner comes as a precompiled assembly for .NET Core, we could either go and package it
        // into a self-contained deployment or treat it as a framework-dependent deployment as intended, let's do the latter
        ...(getXunitConsoleRuntimeConfigNetCoreAppFiles()),
        xunitConsolePackage.getFile(r`/tools/netcoreapp2.0/xunit.runner.utility.netcoreapp10.dll`),
        xunitNetCoreConsolePackage.getFile(r`/lib/netcoreapp2.0/xunit.console.dll`)
    ] : [];
    
// For the DotNetCore run we need to copy a bunch more files:
function additionalRuntimeContent(args: Managed.TestArguments) : Deployment.DeployableItem[] {
    return isDotNetCore(qualifier.targetFramework) ? additionalNetCoreRuntimeContent : [];
}

function runTest(args : TestRunArguments) : File[] {
    // Creating output files
    let logFolder = Context.getNewOutputDirectory('xunit-logs');
    let xmlResultFile = p`${logFolder}/xunit.results.xml`;

    if (Context.getCurrentHost().os === "win" &&
        args.unsafeTestRunArguments && 
        args.unsafeTestRunArguments.runWithUntrackedDependencies){
            args = args.merge({
                tools: {
                    wrapExec: wrapInUntrackedCmd,
                }
            });
    }
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

function wrapInUntrackedCmd(executeArguments: Transformer.ExecuteArguments) : Transformer.ExecuteArguments
{
    // Since we are going to untrack these processes the sealed directories will not be dynamically tracked
    // So attempt to statically list all the files for now
    let  staticDirectoryContents = executeArguments
        .dependencies
        .mapMany(dependency =>
            isStaticDirectory(dependency) ? dependency.contents : []
        );

    return Object.merge<Transformer.ExecuteArguments>(
        executeArguments, 
        {
            tool: {
                exe: Environment.getFileValue("COMSPEC"),
            },
            unsafe: {
                hasUntrackedChildProcesses: true
            },
            arguments: [
                Cmd.argument("/D"),
                Cmd.argument("/C"),
                Cmd.argument(Artifact.input(executeArguments.tool.exe))
                ].prependWhenMerged(),
            dependencies: staticDirectoryContents,
            tags: ["test", "telemetry:xUnitUntracked"]
        });
}

function isStaticDirectory(item: Transformer.InputArtifact) : item is StaticDirectory {
    const itemType = typeof item;
    switch (itemType) {
        case "FullStaticContentDirectory":
        case "PartialStaticContentDirectory":
        case "SourceAllDirectory":
        case "SourceTopDirectory": 
        case "SharedOpaqueDirectory":
        case "ExclusiveOpaqueDirectory": 
        case "StaticDirectory": 
            return true;
        default: 
            false;
    }
}

