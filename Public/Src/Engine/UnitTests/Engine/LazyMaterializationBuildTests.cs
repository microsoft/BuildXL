// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Engine
{
    [Trait("Category", "LazyMaterializationBuildTests")]
    [Trait("Category", "WindowsOSOnly")] // depends on csc.exe
    [Feature(Features.LazyOutputMaterialization)]
    public sealed class LazyMaterializationBuildTests : IncrementalBuildTestBase
    {
        /* The build used for tests in this class generate the following layout
         *
         * \HelloWorldFinal.exe.config (source)
         * \HelloWorldFinal.in (source)
         * \HelloWorldIntermediate.exe.config (source)
         * \HelloWorldIntermediate1.in (source)
         * \HelloWorldIntermediate2.in (source)
         *
         * \obj\Common\HelloWorldFinalAssembly\csc\HelloWorldFinal.exe (output)
         * \obj\Common\HelloWorldIntermediateAssembly\csc\HelloWorldIntermediate.Exe (output)
         *
         * \obj\deploy\bin\HelloWorldFinal.exe (output)
         * \obj\deploy\bin\HelloWorldFinal.exe.config (output)
         * \obj\deploy\bin\HelloWorldIntermediate.Exe (output)
         * \obj\deploy\bin\HelloWorldIntermediate.exe.config (output)
         *
         * \obj\HelloWorld.cs (output)
         * \obj\HelloWorldFinal.out (output)
         * \obj\HelloWorldIntermediate.out (output)
         */
        public LazyMaterializationBuildTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Trait("Category", "MiniBuildTester")] // relies on csc deployment.
        [Fact]
        public void RebuildAfterCleanObjectsWithoutLazyOutputMaterialization()
        {
            BeforeRebuild("Build #1");
            Configuration.Schedule.EnableLazyOutputMaterialization = false;

            BuildCounters counters = Build("Build #2");

            // Execute HelloWorldFinal.exe.
            counters.VerifyNumberOfPipsExecuted(1);

            // Csc-compile HelloWorldIntermediate.exe, HelloWorldFinal.exe.
            // Running HelloWorldIntermediate.exe.
            counters.VerifyNumberOfProcessPipsCached(3);

            counters.VerifyNumberOfCachedOutputsUpToDate(0);

            // Because obj\ was destroyed.
            // Fetch HelloWorldIntermediate.exe, HelloWorldFinal.exe.
            // Fetch HelloWorldIntermediate.out.
            counters.VerifyNumberOfCachedOutputsCopied(3);

            // Deploy HelloWorldIntermediate.exe, HelloWolrdIntermediate.exe.config.
            // Deploy HelloWorldFinal.exe, HelloWolrdFinal.exe.config.
            // Produce HelloWorldFinal.out.
            // Produce HelloWorld.cs
            counters.VerifyNumberOfOutputsProduced(6);
        }

        [Trait("Category", "MiniBuildTester")] // relies on csc deployment.
        [Fact]
        public void RebuildAfterCleanObjectsWithLazyOutputMaterialization()
        {
            BeforeRebuild("Build #1");
            Configuration.Schedule.EnableLazyOutputMaterialization = true;

            Configuration.Schedule.EnableLazyWriteFileMaterialization = true;
            Configuration.Engine.DefaultFilter = @"output='*\HelloWorldFinal.out'";

            BuildCounters counters = Build("Build #2");

            // Execute HelloWorldFinal.exe.
            counters.VerifyNumberOfPipsExecuted(1);

            // Cache hit for everything else.
            counters.VerifyNumberOfProcessPipsCached(TotalPips - 1);
            counters.VerifyNumberOfPipsBringContentToLocal(TotalPips - 1);

            counters.VerifyNumberOfCachedOutputsUpToDate(0);

            // Intermediates which are inputs to executed process are materialized from cache
            // \obj\HelloWorldIntermediate.out (output)
            counters.VerifyNumberOfCachedOutputsCopied(1);

            // Copy file outputs (actually deployed from cache but CopyFile outputs are logged as produced):
            // \obj\deploy\bin\HelloWorldFinal.exe(output)
            // \obj\deploy\bin\HelloWorldFinal.exe.config(output)

            // Output of executed process (HelloWorldFinal.exe):
            // \obj\HelloWorldFinal.out (output)

            // Write file output (NOT MATERIALIZED due to lazy materialization):
            // \obj\HelloWorld.cs (output)
            counters.VerifyNumberOfOutputsProduced(Configuration.Schedule.EnableLazyWriteFileMaterialization ? 3 : 4);
        }

        private void BeforeRebuild(string testMarker)
        {
            SetupTestState();
            EagerCleanBuild(testMarker);

            var objectDirectoryPath = Configuration.Layout.ObjectDirectory.ToString(PathTable);

            if (Directory.Exists(objectDirectoryPath))
            {
                FileUtilities.DeleteDirectoryContents(objectDirectoryPath, deleteRootDirectory: true);
            }

            string bin = GetFullPath("bin");

            if (Directory.Exists(bin))
            {
                FileUtilities.DeleteDirectoryContents(bin, deleteRootDirectory: true);
            }

            const string AddedSuffix = "?";

            File.WriteAllText(GetBuildPaths(Configuration, PathTable).HelloWorldFinalIn, AddedSuffix);
        }

        private sealed class Paths
        {
            public string HelloWorldIntermediateExeConfig;
            public string HelloWorldFinalExeConfig;
            public string HelloWorldFinalOut;
            public string HelloWorldIntermediateIn1;
            public string HelloWorldIntermediateIn2;
            public string HelloWorldFinalIn;
        }

        protected override string GetSpecContents()
        {
            AddCscDeployemnt();

            const string spec = @"
import {Artifact, Cmd, Tool, Transformer} from 'Sdk.Transformers';
import * as Deployment from 'Sdk.Deployment';
import * as Csc from 'Sdk.Managed.Tools.Csc';

const helloWorldCs = Transformer.writeAllLines(
    p`obj/HelloWorld.cs`,
    [
            'using System;',
            'using System.IO;',
            'namespace HelloWorld',
            '{',
            '    class Program',
            '    {',
            '        static void Main(string[] args)',
            '        {',
            '            string x = File.ReadAllText(args[0]);',
            '            string y = File.ReadAllText(args[1]);',
            '            File.WriteAllText(args[2], x + y);',
            '        }',
            '    }',
            '}',
    ]);

function runTool(exeFileName: string, args: Argument[]) : File {

    const assembly = Csc.compile({
        outputPath: p`obj/cscOutput/${exeFileName}`,
        targetType: 'exe',
        sources: [
            helloWorldCs,
        ],
    });

    const deployment = Deployment.deployToDisk({
        targetDirectory: d`obj/deploy/bin`,
        definition: {
            contents: [
                f`./${exeFileName + '.config'}`,
                assembly.binary,
            ],
        },
        primaryFile: exeFileName,
    });

    const toolDefinition: Transformer.ToolDefinition = {
        exe: deployment.primaryFile,
        runtimeDependencies: deployment.contents.contents,
        dependsOnWindowsDirectories: true,
        dependsOnAppDataDirectory: true,
    };

    const result = Transformer.execute({
        tool: toolDefinition,
        workingDirectory: d`.`,
        arguments: args,
    });

    return result.getOutputFiles()[0];
}

const intermediate = runTool(
    'HelloWorldIntermediate.exe',
    [
        Cmd.argument(Artifact.input(f`HelloWorldIntermediate1.in`)),
        Cmd.argument(Artifact.input(f`HelloWorldIntermediate2.in`)),
        Cmd.argument(Artifact.output(p`obj/HelloWorldIntermediate.out`)),
    ]);

const final = runTool(
    'HelloWorldFinal.exe',
    [
        Cmd.argument(Artifact.input(intermediate)),
        Cmd.argument(Artifact.input(f`HelloWorldFinal.in`)),
        Cmd.argument(Artifact.output(p`obj/HelloWorldFinal.out`)),
    ]);
";

            return spec;
        }

        private void AddCscDeployemnt()
        {
            AddSdk(GetTestDataValue("MicrosoftNetCompilersSdkLocation"));

            var spec = @"
import {Artifact, Cmd, Tool, Transformer} from 'Sdk.Transformers';

const pkgContents = importFrom('Microsoft.Net.Compilers').pkg.contents;

@@public
export const tool = {
    exe: pkgContents.getFile(r`tools/csc.exe`),
    description: 'Microsoft C# Compiler',
    runtimeDependencies: pkgContents.contents,
    untrackedDirectoryScopes: [
        d`${Context.getMount('ProgramData').path}`,
    ],
    dependsOnWindowsDirectories: true,
    prepareTempDirectory: true,
};

@@public
export interface Arguments {
    outputPath: Path;
    targetType?: 'library' | 'exe';
    sources: File[];
}

@@public
export interface Result {
    binary: File
}

@@public
export function compile(args: Arguments) : Result {
    let result = Transformer.execute({
        tool: tool,
        workingDirectory: d`.`,
        arguments: [
            Cmd.option('/out:',               Artifact.output(args.outputPath)),
            Cmd.option('/target:',            args.targetType),
            Cmd.files(args.sources),
        ],
    });

    return {
        binary: result.getOutputFile(args.outputPath),
    };
}
";
            AddModule("Sdk.Managed.Tools.Csc", ("cscHack.dsc", spec));
        }


        private static Paths GetBuildPaths(IConfiguration config, PathTable pathTable)
        {
            var sourceDirectoryPath = config.Layout.SourceDirectory.ToString(pathTable);
            var objectDirectoryPath = config.Layout.ObjectDirectory.ToString(pathTable);

            return new Paths
                   {
                       HelloWorldIntermediateExeConfig = Path.Combine(sourceDirectoryPath, "HelloWorldIntermediate.exe.config"),
                       HelloWorldFinalExeConfig = Path.Combine(sourceDirectoryPath, "HelloWorldFinal.exe.config"),
                       HelloWorldFinalOut = Path.Combine(objectDirectoryPath, "HelloWorldFinal.out"),
                       HelloWorldIntermediateIn1 = Path.Combine(sourceDirectoryPath, "HelloWorldIntermediate1.in"),
                       HelloWorldIntermediateIn2 = Path.Combine(sourceDirectoryPath, "HelloWorldIntermediate2.in"),
                       HelloWorldFinalIn = Path.Combine(sourceDirectoryPath, "HelloWorldFinal.in")
                   };
        }

        protected override void WriteInitialSources()
        {
            const string HelloWorldExeConfig = @"
<?xml version='1.0' encoding='utf-8'?>
<configuration>
</configuration>
";
            AddFile("HelloWorldIntermediate.exe.config", HelloWorldExeConfig);
            AddFile("HelloWorldFinal.exe.config", HelloWorldExeConfig);

            const string HelloWorldIntermediateContent1 = "BuildXL";
            const string HelloWorldIntermediateContent2 = "!";
            const string HelloWorldFinalContent = "!";

            AddFile("HelloWorldIntermediate1.in", HelloWorldIntermediateContent1);
            AddFile("HelloWorldIntermediate2.in", HelloWorldIntermediateContent2);
            AddFile("HelloWorldFinal.in", HelloWorldFinalContent);
        }

        protected override int TotalPips => 4;

        protected override int TotalPipOutputs => 9;

        protected override void VerifyOutputsAfterBuild(IConfiguration config, PathTable pathTable)
        {
            var buildPaths = GetBuildPaths(config, pathTable);
            XAssert.AreEqual(
                File.ReadAllText(buildPaths.HelloWorldFinalOut),
                File.ReadAllText(buildPaths.HelloWorldIntermediateIn1) + File.ReadAllText(buildPaths.HelloWorldIntermediateIn2) +
                File.ReadAllText(buildPaths.HelloWorldFinalIn));
        }
    }
}
