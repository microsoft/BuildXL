// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Cmd, Artifact, Transformer} from "Sdk.Transformers";

namespace LinuxTestProcess {
    export declare const qualifier : {
        configuration: "debug" | "release",
        targetRuntime: "linux-x64"
    };

    @@public
    export function exe() : DerivedFile {
        if (Context.getCurrentHost().os !== "unix") {
            return undefined;
        }

        const gxxTool : Transformer.ToolDefinition = {
            exe: f`/usr/bin/g++`,
            dependsOnCurrentHostOSDirectories: true,
            prepareTempDirectory: true,
            untrackedDirectoryScopes: [ d`/lib` ],
            runtimeDependencies: [f`/usr/lib64/ld-linux-x86-64.so.2`]
        };
        const outDir = Context.getNewOutputDirectory(gxxTool.exe.name);
        const exeFile = p`${outDir}/LinuxTestProcess`;
        const headerFiles = [ f`syscalltests.hpp` ];
        const srcFiles = [ f`main.cpp`, f`syscalltests.cpp` ];

        const result = Transformer.execute({
            tool: gxxTool,
            workingDirectory: outDir,
            dependencies: [...srcFiles, ...headerFiles],
            arguments: [
                Cmd.args(Artifact.inputs(srcFiles)),
                Cmd.option("-o ", Artifact.output(exeFile)),
                Cmd.option("-I ", Artifact.none(d`.`)),
            ]
        });

        return result.getOutputFile(exeFile);
    }
}