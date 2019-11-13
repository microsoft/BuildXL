// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import {CoreRT} from "Sdk.MacOS";
import * as Managed from "Sdk.Managed";
import * as Shared from "Sdk.Managed.Shared";
import * as Frameworks from "Sdk.Managed.Frameworks";

export declare const qualifier: Managed.TargetFrameworks.CurrentMachineQualifier;

const pkgContents = importFrom("BuildXL.Tools.AppHostPatcher").Contents.all;

function contentFilter(file: File): boolean {
    return Context.getCurrentHost().os === "macOS"
        ? file.extension === a`.dylib` || file.extension === a`.a` || file.extension === a`.h`
        : file.extension === a`.dll` || file.extension === a`.lib` || file.extension === a`.h`;
}

const patcherExecutable = Context.getCurrentHost().os === "macOS"
    ? pkgContents.getFile(r`tools/osx-x64/AppHostPatcher`)
    : pkgContents.getFile(r`tools/win-x64/AppHostPatcher.exe`);

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

    const contents = targetsWindows
        ? importFrom("Microsoft.NETCore.App.Host.win-x64").Contents.all
        : importFrom("Microsoft.NETCore.App.Host.osx-x64").Contents.all;

    // Pick the apphost based on the target OS, not the current OS
    const apphostBinary = targetsWindows
        ? contents.getFile(r`/runtimes/win-x64/native/apphost.exe`)
        : contents.getFile(r`/runtimes/osx-x64/native/apphost`);

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
        ],
    });

    return {
        contents: [
            result.getOutputFile(outputPath),
            ...contents.getContent().filter(f => contentFilter(f)),
        ]
    };
}

@@public
export interface Arguments {
    binary: File,
    targetRuntimeVersion: Managed.RuntimeVersion,
}

export interface Result {
    contents: File[],
}

