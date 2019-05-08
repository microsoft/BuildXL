// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";
import * as Managed from "Sdk.Managed.Shared";
import * as MacOS from "Sdk.MacOS";

const pkgContents = importFrom("Grpc.Tools").Contents.all;

@@public
export const tool: Transformer.ToolDefinition = {
    exe: pkgContents.getFile(
        Context.getCurrentHost().os === "win"
            ? r`tools/windows_x64/protoc.exe`
            : r`tools/macosx_x64/protoc`),
    dependsOnCurrentHostOSDirectories: true
};

/**
 * Generates the protobuf files.
 * For now this is simply hardcoded to generate C# on windows
 * For production this should be extended to support all languages and multiple platforms
 */
@@public
export function generate(args: Arguments) : Result {

    const outputDirectory = Context.getNewOutputDirectory("protobuf");
    const arguments : Argument[] = [
        Cmd.option("--proto_path ", Artifact.none(args.proto.parent)),
        Cmd.option("--csharp_out ", Artifact.none(outputDirectory)),
        Cmd.files([args.proto]),
        Cmd.option("--grpc_out ", Artifact.none(outputDirectory)),
        Cmd.option("--plugin=protoc-gen-grpc=", Artifact.input(pkgContents.getFile(
            Context.getCurrentHost().os === "win"
                ? r`tools/windows_x64/grpc_csharp_plugin.exe`
                : r`tools/macosx_x64/grpc_csharp_plugin`)
            )
        ),
    ];

    let mainCsFile = p`${outputDirectory}/${args.proto.nameWithoutExtension + ".cs"}`;
    let grpcCsFile = p`${outputDirectory}/${args.proto.nameWithoutExtension + "Grpc.cs"}`;

    let result = Transformer.execute({
        tool: args.tool || tool,
        arguments: arguments,
        workingDirectory: outputDirectory,
        outputs: [
            mainCsFile,
            grpcCsFile,
        ]
    });

    return {
        sources: [
            result.getOutputFile(mainCsFile),
            result.getOutputFile(grpcCsFile),
        ],
    };
}

@@public
export interface Arguments extends Transformer.RunnerArguments{
    proto: File,
}

@@public
export interface Result {
    sources: File[],
}
