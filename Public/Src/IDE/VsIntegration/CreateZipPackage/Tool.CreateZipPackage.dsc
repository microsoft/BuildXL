// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import * as Deployment from "Sdk.Deployment";

namespace CreateZipPackage
{
    export declare const qualifier: {};

    namespace Tool 
    {
        export declare const qualifier: BuildXLSdk.TargetFrameworks.CurrentMachineQualifier;

        const exe = BuildXLSdk.executable({
            assemblyName: "CreateZipPackage",
            rootNamespace: "BuildXL.IDE.CreateZipPackage",
            sources: globR(d`.`, "*.cs"),
            references: [
                ...addIfLazy(BuildXLSdk.isFullFramework, () => [
                    NetFx.System.IO.Compression.dll,
                    NetFx.System.IO.Compression.FileSystem.dll,
                ]),
                importFrom("BuildXL.Utilities").ToolSupport.dll,
            ],
        });

        export const deployed = BuildXLSdk.deployManagedTool({
            tool: exe,
            options: {
                prepareTempDirectory: true
            },
        });
    }

    @@public
    export interface Arguments {
        inputDirectory: StaticDirectory;
        outputFileName: string;
        useUriEncoding?: boolean;
        fixUnixPermissions?: boolean;
        additionalDependencies?: Transformer.InputArtifact[];
    }

    @@public
    export function zip(args: Arguments): DerivedFile {
        const wd = Context.getNewOutputDirectory("zip");
        const outFile = wd.combine(args.outputFileName);

        const cmdLineArguments = [
            Cmd.option("/inputDirectory:",  Artifact.input(args.inputDirectory)),
            Cmd.option("/outputFile:",      Artifact.output(outFile)),
            Cmd.sign("/uriEncoding",        args.useUriEncoding),
            Cmd.sign("/fixUnixPermissions", args.fixUnixPermissions),
        ];

        const tool = Tool.withQualifier(BuildXLSdk.TargetFrameworks.currentMachineQualifier).deployed;

        const result = Transformer.execute({
            tool: tool, 
            workingDirectory: wd, 
            arguments: cmdLineArguments,
            dependencies: args.additionalDependencies
        });

        return result.getOutputFile(outFile);
    }
}