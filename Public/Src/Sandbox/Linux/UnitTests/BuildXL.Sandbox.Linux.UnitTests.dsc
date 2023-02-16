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

    interface BoostTest {
        exeName: PathAtom;
        sourceFiles: File[];
        includeDirectories?: Directory[];
    }

    const sandboxSrcDirectory = Directory.fromPath(p`.`.parent);
    const boostTests : BoostTest[] = [
        {
            exeName: a`interpose`,
            sourceFiles: [ f`interpose.cpp` ]
        },
        {
            exeName: a`observer_utililities_test`,
            sourceFiles: [ f`observer_utililities_test.cpp`, f`${sandboxSrcDirectory.path}/observer_utilities.cpp` ],
            includeDirectories: [ sandboxSrcDirectory ]
        }
    ];

    const isLinux = Context.getCurrentHost().os === "unix";
    const gxx = getTool("g++");
    const env = getTool("env");
    const libDetours = Sandbox.libDetours;
    const boostLibDir = boost.Contents.all.ensureContents({subFolder: r`lib/native/include`});

    @@public
    export const tests = boostTests.map(s => runBoostTest(compileForBoost(s)));

    function compileForBoost(testSpec: BoostTest) : DerivedFile
    {
        if (!isLinux) return undefined;

        const compiler = gxx;
        const isDebug = qualifier.configuration === "debug";
        const outDir = Context.getNewOutputDirectory(compiler.exe.name);
        const exeFile = p`${outDir}/${testSpec.exeName}`;
        let flattenedHeaders = [];
        const headers = testSpec.includeDirectories
            ? testSpec.includeDirectories.map((d, i) => glob(d, "*.hpp"))
            : [];
        for (let headerSet of headers) {
            flattenedHeaders = flattenedHeaders.concat(...headerSet);
        }

        const result = Transformer.execute({
            tool: compiler,
            workingDirectory: outDir,
            dependencies: [
                boostLibDir,
                ...testSpec.sourceFiles,
                ...flattenedHeaders
            ],
            arguments: [
                Cmd.options("-I ", [
                    Artifact.none(boostLibDir),
                    ...(testSpec.includeDirectories ? testSpec.includeDirectories.map((d, i) => Artifact.none(d)) : [])
                ]),
                Cmd.args(testSpec.sourceFiles.map((s, i) => Artifact.input(s))),
                Cmd.option("-o ", Artifact.output(exeFile)),
                ...addIf(isDebug, Cmd.argument("-g")),
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
            untrackedFiles: [],
            runtimeDependencies: [f`/usr/lib64/ld-linux-x86-64.so.2`]
        };
    }
}