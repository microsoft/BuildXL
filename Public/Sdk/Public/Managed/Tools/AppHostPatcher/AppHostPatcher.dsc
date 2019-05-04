// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import * as Deployment from "Sdk.Deployment";
import * as Managed from "Sdk.Managed";
import * as Shared from "Sdk.Managed.Shared";
import * as MacOS from "Sdk.MacOS";
import * as Frameworks from "Sdk.Managed.Frameworks";

export declare const qualifier: Managed.TargetFrameworks.CurrentMachineQualifier;

const patcher: Transformer.ToolDefinition = (() => {
    const compileArgs = <Managed.Arguments>{ 
        assemblyName: "AppHostPatcher",
        sources: globR(d`.`, "*.cs")
    };

    const tool = Context.getCurrentHost().os === "macOS"
        ? Managed.nativeExecutable(compileArgs)
        : Managed.executable(compileArgs);

    return Managed.deployManagedTool({
        tool: tool,
        options: {
            dependsOnCurrentHostOSDirectories: true
        }
    });
})();

@@public
export function patchBinary(args: Arguments) : Result {
    const targetsWindows = args.targetRuntimeVersion === "win-x64";

    // Pick the apphost based on the target OS, not the current OS
    const apphostBinary = targetsWindows
        ? importFrom("runtime.win-x64.Microsoft.NETCore.DotNetAppHost").Contents.all.getFile(r`/runtimes/win-x64/native/apphost.exe`)
        : importFrom("runtime.osx-x64.Microsoft.NETCore.DotNetAppHost").Contents.all.getFile(r`/runtimes/osx-x64/native/apphost`);

    const arguments : Argument[] = [
        Cmd.argument(Artifact.input(apphostBinary)),
        Cmd.argument(Artifact.input(args.binary)),
    ];

    const wd = Context.getNewOutputDirectory("AppHostPatcher");
    const outputFileName = args.binary.nameWithoutExtension + (targetsWindows ? ".exe" : "");
    const outputPath = p`${wd}/Output/${outputFileName}`;

    let exeArgs = <Transformer.ExecuteArguments>{
        tool: patcher,
        arguments: arguments,
        workingDirectory: wd,
        outputs: [
            outputPath,
        ]
    };

    if (Context.getCurrentHost().os === "win") {
        exeArgs = importFrom("Sdk.Managed.Frameworks.NetCoreApp2.2").withQualifier({targetFramework: "netcoreapp2.2"}).wrapInDotNetExeForCurrentOs(exeArgs);
    }

    const result = Transformer.execute(exeArgs);

    return {
        binary: result.getOutputFile(outputPath)
    };
}

@@public
export interface Arguments {
    binary: File,
    targetRuntimeVersion: Managed.RuntimeVersion,
}

export interface Result {
    binary: File,
}

