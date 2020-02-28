// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

namespace Interop {
    @@public
    export const opNamesAutoGen = genOpNamesCSharpFile(f`${Context.getMount("Sandbox").path}/MacOs/Sandbox/Src/Kauth/OpNames.hpp`);

    const exe = BuildXLSdk.executable({
        assemblyName: "BuildXL.Interop.TmpOpNameGenerator",
        sources: globR(d`.`, "*.cs"),
        allowUnsafeBlocks: true,
    });

    export const deployed = BuildXLSdk.deployManagedTool({
        tool: exe,
        options: {
            prepareTempDirectory: true,
        },
    });

    function genOpNamesCSharpFile(inputHppFile: SourceFile): DerivedFile {
        const tool = Interop.withQualifier(BuildXLSdk.TargetFrameworks.MachineQualifier.current).deployed;
        const outDir = Context.getNewOutputDirectory("op-name-out");
        const consoleOutPath = p`${outDir}/FileOperation.g.cs`;
        const result = Transformer.execute({
            tool: tool,
            arguments: [
                Cmd.argument(Artifact.input(inputHppFile))
            ],
            workingDirectory: outDir,
            consoleOutput: consoleOutPath,
            tags: ["codegen"]
        });

        return result.getOutputFile(consoleOutPath);
    }
}
