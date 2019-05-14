// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import * as Deployment from "Sdk.Deployment";

namespace Tool.CreateZipPackage {

    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    export const exe = BuildXLSdk.executable({
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
        defineConstants: [
            ...addIf(qualifier.targetFramework === "net472" || BuildXLSdk.isDotNetCoreBuild, "FEATURE_EXTENDED_ATTR")
        ]
    });

    export const deployed = BuildXLSdk.deployManagedTool({
        tool: exe,
        options: {
            prepareTempDirectory: true
        },
    });

    export interface Arguments {
        inputDirectory: StaticDirectory;
        outputFileName: string;
        useUriEncoding?: boolean;
        fixUnixPermissions?: boolean;
    }

    @@public
    export function run(args: Arguments): DerivedFile {
        const wd = Context.getNewOutputDirectory("zip");
        const outFile = wd.combine(args.outputFileName);

        const cmdLineArguments = [
            Cmd.option("/inputDirectory:",  Artifact.input(args.inputDirectory)),
            Cmd.option("/outputFile:",      Artifact.output(outFile)),
            Cmd.sign("/uriEncoding",        args.useUriEncoding),
            Cmd.sign("/fixUnixPermissions", args.fixUnixPermissions),
        ];

        const tool = CreateZipPackage.withQualifier(BuildXLSdk.TargetFrameworks.currentMachineQualifier).deployed;

        const result = Transformer.execute({tool: tool, workingDirectory: wd, arguments: cmdLineArguments});

        return result.getOutputFile(outFile);
    }
}
