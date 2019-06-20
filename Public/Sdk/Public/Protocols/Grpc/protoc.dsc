// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";
import * as Managed from "Sdk.Managed.Shared";
import * as MacOS from "Sdk.MacOS";

const pkgContents = importFrom("Grpc.Tools").Contents.all;
const includesFolder = d`${pkgContents.root}/build/native/include`;

@@public
export const tool: Transformer.ToolDefinition = {
    exe: pkgContents.getFile(
        Context.getCurrentHost().os === "win"
            ? r`tools/windows_x64/protoc.exe`
            : r`tools/macosx_x64/protoc`),
    dependsOnCurrentHostOSDirectories: true
};

/**
 * Standard includes for Protobufs.
 */
@@public
export const includes = Transformer.sealPartialDirectory(
    includesFolder, 
    pkgContents.getContent().filter(file => file.isWithin(includesFolder))
);

/**
 * Generates the protobuf files.
 * For now this is simply hardcoded to generate C# on windows
 * For production this should be extended to support all languages and multiple platforms
 */
@@public
export function generate(args: Arguments) : Result {

    let resultSources : File[] = [];

    let filesToProcess : {file: File, isRpc: boolean }[] = [];
    
    if (args.proto) {
        for (let file of args.proto) {
            filesToProcess = filesToProcess.push({file: file, isRpc: false});
        }
    }

    if (args.rpc) {
        for (let file of args.rpc) {
            filesToProcess = filesToProcess.push({file: file, isRpc: true});
        }
    }

    for (let fileToProcess of filesToProcess) {
        const outputDirectory = Context.getNewOutputDirectory("protobuf");
        const arguments : Argument[] = [
            Cmd.option("--proto_path ", Artifact.none(fileToProcess.file.parent)),
            Cmd.option("--csharp_out ", Artifact.none(outputDirectory)),
            Cmd.files([fileToProcess.file]),
            ...addIf(fileToProcess.isRpc,
                Cmd.option("--grpc_out ", Artifact.none(outputDirectory)),
                Cmd.option("--plugin=protoc-gen-grpc=", Artifact.input(pkgContents.getFile(
                    Context.getCurrentHost().os === "win"
                        ? r`tools/windows_x64/grpc_csharp_plugin.exe`
                        : r`tools/macosx_x64/grpc_csharp_plugin`)
                    )
                )
            ),
            Cmd.options("--proto_path=", Artifact.inputs(args.includes)),
        ];

        const targetFileName = fileToProcess.file.nameWithoutExtension.toString().replace("_", "");
        const mainCsFile = p`${outputDirectory}/${targetFileName + ".cs"}`;
        const grpcCsFile = p`${outputDirectory}/${targetFileName + "Grpc.cs"}`;

        const result = Transformer.execute({
            tool: args.tool || tool,
            arguments: arguments,
            workingDirectory: outputDirectory,
            outputs: [
                mainCsFile,
                ...addIf(fileToProcess.isRpc,
                    grpcCsFile
                ),
            ],
            dependencies: filesToProcess.map(fileToProcess => fileToProcess.file),
        });

        resultSources = resultSources.push(result.getOutputFile(mainCsFile));
        if (fileToProcess.isRpc) {
            resultSources = resultSources.push(result.getOutputFile(grpcCsFile));
        }
    }

    return {
        sources: resultSources,
    };
}

@@public
export interface Arguments extends Transformer.RunnerArguments{
    proto?: File[],
    rpc?: File[],
    includes?: StaticDirectory[],
}

@@public
export interface Result {
    sources: File[],
}
