// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import {CoreRT} from "Sdk.MacOS";
import * as Managed from "Sdk.Managed";
import * as Shared from "Sdk.Managed.Shared";
import * as Frameworks from "Sdk.Managed.Frameworks";

export declare const qualifier: Managed.TargetFrameworks.MachineQualifier.Current;

const pkgContents = importFrom("BuildXL.Tools.AppHostPatcher").Contents.all;
const currentOs = Context.getCurrentHost().os;
const isMacOS = currentOs === "macOS";
const isLinuxOS = currentOs === "unix";
const isWinOS = currentOs === "win";

function contentFilter(file: File): boolean {
    return isMacOS
            ? (file.extension === a`.dylib` || file.extension === a`.a` || file.extension === a`.h`)
            :
        isLinuxOS
            ? (file.extension === a`.so` || file.extension === a`.a` || file.extension === a`.o`)
            :
        isWinOS
            ? (file.extension === a`.dll` || file.extension === a`.lib` || file.extension === a`.h`)
            : Contract.fail("Unknown os: " + currentOs);
}

const patcherExecutable =
    isWinOS   ? pkgContents.getFile(r`tools/win-x64/AppHostPatcher.exe`) :
    isMacOS   ? pkgContents.getFile(r`tools/osx-x64/AppHostPatcher`) :
    isLinuxOS ? pkgContents.getFile(r`tools/linux-x64/AppHostPatcher`) :
    undefined;

const patcher: Transformer.ToolDefinition = {
    exe: patcherExecutable,
    dependsOnCurrentHostOSDirectories: true,
    runtimeDirectoryDependencies: [
        pkgContents
    ]
};

@@public
export function patchBinary(args: Arguments) : Result {
    const targetsWindows = args.targetRuntimeVersion === "win-x64";
    const contents: StaticDirectory = 
        args.targetRuntimeVersion === "win-x64"
            ? importFrom("Microsoft.NETCore.App.Host.win-x64").Contents.all
            :
        args.targetRuntimeVersion === "linux-x64"
            ? importFrom("Microsoft.NETCore.App.Host.linux-x64").Contents.all
            :
        args.targetRuntimeVersion === "osx-x64"
            ? importFrom("Microsoft.NETCore.App.Host.osx-x64").Contents.all
            : Contract.fail("Unknown target runtime: " + args.targetRuntimeVersion);

    // Pick the apphost based on the target OS, not the current OS
    const apphostBinary = targetsWindows
        ? contents.getFile(r`/runtimes/win-x64/native/apphost.exe`)
        : contents.getFile(r`/runtimes/${args.targetRuntimeVersion}/native/apphost`);

    const arguments : Argument[] = [
        Cmd.argument(Artifact.input(apphostBinary)),
        Cmd.argument(Artifact.input(args.binary)),
    ];

    const wd = Context.getNewOutputDirectory("AppHostPatcher");
    const outputFileName = args.binary.nameWithoutExtension + (targetsWindows ? ".exe" : "");
    const outputPath = p`${wd}/Output/${outputFileName}`;

    const result = Transformer.execute({
        tool: patcher,
        arguments: arguments,
        workingDirectory: wd,
        outputs: [
            outputPath,
        ],
        environmentVariables: [
            { name: "COMPlus_EnableDiagnostics", value: "0" }, // Disables debug pipe creation
        ]
    });

    return {
        contents: [
            ...contents.getContent().filter(f => contentFilter(f)),
        ],
        patchOutputFile: result.getOutputFile(outputPath)
    };
}

@@public
export interface Arguments {
    binary: File,
    targetRuntimeVersion: Managed.RuntimeVersion,
}

/**
 * Binary files that AppHostPatcher patched. These files are part of Assembly.runtimeContent
 * @patchOutputFile: This file is the executable file. Also part of 'contents' as a way to identify the patched output file.
 */
@@public
export interface Result {
    contents: File[],
    patchOutputFile: File,
}

