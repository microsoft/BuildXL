// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Cmd, Artifact, Transformer} from "Sdk.Transformers";
import {Sandbox} from "BuildXL.Sandbox.Linux";
import * as boost from "boost";

namespace UnitTests
{
    export declare const qualifier : {
        configuration: "debug" | "release",
        targetRuntime: "linux-x64"
    };

    const testFiles = glob(d`.`, "*.cpp");
    const isLinux = Context.getCurrentHost().os === "unix";
    const gxx = getTool("g++");
    const env = getTool("env");
    const libDetours = Sandbox.libDetours;
    const boostLibDir = boost.Contents.all.ensureContents({subFolder: r`lib/native/include`});

    @@public
    export const tests = testFiles.map(s => runBoostTest(compileForBoost(s)));

    function compileForBoost(srcFile: File) : DerivedFile
    {
        if (!isLinux) return undefined;

        const compiler = gxx;
        const outDir = Context.getNewOutputDirectory(compiler.exe.name);
        const exeFile = p`${outDir}/${srcFile.name.changeExtension("")}`;
        const result = Transformer.execute({
            tool: compiler,
            workingDirectory: outDir,
            dependencies: [boostLibDir],
            arguments: [
                Cmd.option("-I ", Artifact.none(boostLibDir)),
                Cmd.argument(Artifact.input(srcFile)),
                Cmd.option("-o ", Artifact.output(exeFile)),
            ]
        });
        return result.getOutputFile(exeFile);
    }

    function runBoostTest(exeFile: File) : TransformerExecuteResult
    {
        if (!isLinux) return undefined;

        const workDir = Context.getNewOutputDirectory(exeFile.name);
        const outDir = Context.getNewOutputDirectory(exeFile.name);
        return Transformer.execute({
            tool: env,
            workingDirectory: workDir,
            arguments: [
                Cmd.option("LD_PRELOAD=", Artifact.input(libDetours)),
                Cmd.argument(Artifact.input(exeFile)),
            ],
            outputs: [{existence: "optional", artifact: p`${outDir}/dummy`}],
            unsafe: {
                untrackedScopes: [ workDir ],
                // Using nested interpose does not work.
                disableSandboxing: true
            }
        });
    }

    function getTool(toolName: string) : Transformer.ToolDefinition {
        if (!isLinux) return undefined;

        return {
            exe: f`/usr/bin/${toolName}`,
            dependsOnCurrentHostOSDirectories: true,
            prepareTempDirectory: true,
            untrackedDirectoryScopes: [ d`/lib` ],
            untrackedFiles: []
        };
    }
}