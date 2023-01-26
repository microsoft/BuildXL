// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Cmd, Artifact, Transformer} from "Sdk.Transformers";

namespace StaticLinkingTestProcess {
    export declare const qualifier : {
        configuration: "debug" | "release",
        targetRuntime: "linux-x64"
    };

    // TODO work item #1988321 [pgunasekara]: Use Sdk.Native once its updated to include gcc
    @@public
    export function exe(staticallyLink : Boolean) : DerivedFile {
        if (Context.getCurrentHost().os !== "unix") return undefined;

        const gxxTool : Transformer.ToolDefinition = {
            exe: f`/usr/bin/g++`,
            dependsOnCurrentHostOSDirectories: true,
            prepareTempDirectory: true,
            untrackedDirectoryScopes: [ d`/lib` ],
            runtimeDependencies: [f`/usr/lib64/ld-linux-x86-64.so.2`]
        };
        const outDir = Context.getNewOutputDirectory(gxxTool.exe.name);
        const exeFile = p`${outDir}/${staticallyLink ? "TestProcessStaticallyLinked" : "TestProcessDynamicallyLinked"}`;
        const srcFile = f`main.cpp`;

        const result = Transformer.execute({
            tool: gxxTool,
            workingDirectory: outDir,
            dependencies: [ srcFile ],
            arguments: [
                Cmd.argument(Artifact.input(srcFile)),
                Cmd.option("-o ", Artifact.output(exeFile)),
                ...(staticallyLink ? [Cmd.rawArgument("-static")] : []),
                ...(staticallyLink ? [Cmd.rawArgument("-D STATICALLYLINKED")] : [])
            ]
        });

        return result.getOutputFile(exeFile);
    }
}