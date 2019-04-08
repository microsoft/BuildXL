// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

const cmdExe: Transformer.ToolDefinition = {
        exe: f`${Environment.getPathValue("ComSpec")}`,
        dependsOnWindowsDirectories: true,
        untrackedDirectoryScopes: [
            d`${Environment.getPathValue("SystemRoot")}`
        ]
    };

function mklink(file) {
    const outDir = Context.getNewOutputDirectory("mklink");
    let cmd = [
            Cmd.argument("/d"),
            Cmd.argument("/c"),
            Cmd.argument("mklink"),
            Cmd.argument(Artifact.output(p`${outDir}/link`)),
            Cmd.argument(Artifact.input(file))
        ];
     return Transformer.execute({
         tool: cmdExe,
         arguments: cmd,
         workingDirectory: outDir
     });    
}

const x = mklink(f`a.txt`);
const y = mklink(f`a.txt`);
