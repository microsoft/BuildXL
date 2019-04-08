// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

import * as XUnit        from "Sdk.Managed.Testing.XUnit";
import * as Managed      from "Sdk.Managed";
import * as Deployment   from "Sdk.Deployment";

export declare const qualifier : Managed.TargetFrameworks.All;

// This framework wraps the regular xunit framework but does not run the xunit test under the Detours sandbox.
// This one is highly unsafe and should only be used for tests that need to test things that don't work under
// the BuildXL sandbox.

@@public
export const framework : Managed.TestFramework = {
    compileArguments: XUnit.framework.compileArguments,
    additionalRuntimeContent: XUnit.framework.additionalRuntimeContent,
    runTest: runTest,
    name: "XUnitUnDetoured",
};

function runTest(args : XUnit.TestRunArguments) : File[] {
    // Windows sandbox does not support nesting, so we'll have to wrap. 
    // Mac sandbox supports nesting just fine, so no need to wrap
    if (Context.getCurrentHost().os === "win") {
        args = args.merge({
            tools: {
                wrapExec: wrapInUntrackedCmd,
            }
        });
    }
    return XUnit.framework.runTest(args);
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
