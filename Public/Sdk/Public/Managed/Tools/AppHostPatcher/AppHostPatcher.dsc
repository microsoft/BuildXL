// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import * as Managed from "Sdk.Managed";
import * as MacOS from "Sdk.MacOS";

export declare const qualifier: Managed.TargetFrameworks.CurrentMachineQualifier;

const pkgContents = importFrom("BuildXL.Tools.AppHostPatcher").Contents.all;

const patcher: Transformer.ToolDefinition = {
    exe: pkgContents.getFile(r`tools/${qualifier.targetRuntime}/${"AppHostPatcher" + (qualifier.targetRuntime === "win-x64" ? ".exe" : "")}`),
    dependsOnWindowsDirectories: true,
    untrackedDirectoryScopes: [
        ...addIfLazy(Context.getCurrentHost().os === "macOS", () => [
            ...MacOS.untrackedSystemFolderDeps,
            d`${Context.getMount("UserProfile").path}/Microsoft/BuildXL/RestrictedTemp`,
        ])
    ],
    untrackedFiles: [
        ...addIfLazy(Context.getCurrentHost().os === "macOS", () => MacOS.untrackedFiles)
    ],
    runtimeDirectoryDependencies: [
        pkgContents,
        ...addIfLazy(Context.getCurrentHost().os === "macOS", () => MacOS.systemFolderInputDeps),
    ]
};

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

    const result = Transformer.execute({
        tool: patcher,
        arguments: arguments,
        workingDirectory: wd,
        outputs: [
            outputPath,
        ],
        dependencies: [
            ...addIfLazy(Context.getCurrentHost().os === "macOS", () => MacOS.filesAndSymlinkInputDeps)
        ]
    });

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


