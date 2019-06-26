// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Engine;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.EngineTestUtilities;
using Test.BuildXL.TestUtilities;
using Test.DScript.Ast;
using Test.BuildXL.FrontEnd.Ninja.Infrastructure;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Ninja
{
    /// <summary>
    /// Provides facilities to run tests on the engine with Ninja specs
    /// </summary>
    public abstract class NinjaPipExecutionTestBase : DsTestWithCacheBase
    {
        // Default Ninja build file and its location (which we imply to be the project root)
        protected const string DefaultSpecFileName = "build.ninja";
        protected const string DefaultProjectRoot = "ninjabuild";
        private string DefaultSpecFileLocation => $"{DefaultProjectRoot}/{DefaultSpecFileName}";

        /// <summary>
        /// Root to the source enlistment root
        /// </summary>
        protected string SourceRoot { get; }

        // By default the engine runs e2e
        protected virtual EnginePhases Phase => EnginePhases.Execute;

        protected NinjaPipExecutionTestBase(ITestOutputHelper output) : base(output, true)
        {
            RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Core.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Script.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Nuget.ETWLogger.Log);
            
            SourceRoot = Path.Combine(TestRoot, RelativeSourceRoot);
        }

        /// <inheritdoc/>
        protected ICommandLineConfiguration BuildAndGetConfiguration(NinjaSpec spec, bool includeProjectRoot = true, bool includeSpecFile = true)
        {
            return base.Build()
                .Configuration(NinjaPrelude(targets: spec.Targets, includeProjectRoot: includeProjectRoot, includeSpecFile: includeSpecFile))
                .AddSpec(Path.Combine(SourceRoot, DefaultProjectRoot, DefaultSpecFileName), spec.Content)
                .PersistSpecsAndGetConfiguration();
        }

        protected BuildXLEngineResult RunEngineWithConfig(ICommandLineConfiguration config, TestCache testCache = null)
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TestOutputDirectory))
            {
                var appDeployment = CreateAppDeployment(tempFiles);

                // Set the specified phase
                ((CommandLineConfiguration)config).Engine.Phase = Phase;
                ((CommandLineConfiguration)config).Sandbox.FileSystemMode = FileSystemMode.RealAndMinimalPipGraph;

                var engineResult = CreateAndRunEngine(
                    config,
                    appDeployment,
                    testRootDirectory: null,
                    rememberAllChangedTrackedInputs: true,
                    engine: out _,
                    testCache: testCache);

                return engineResult;
            }
        }

        /// <summary>
        /// Returns a project that just echoes 'Hello World' to an output file
        /// </summary>
        protected NinjaSpec CreateHelloWorldProject(string outputFileName)
        {
            var content =
$@"rule r
    command = cmd /C ""echo Hello World > $out""
build {outputFileName}: r
build all: phony {outputFileName}
";
            return new NinjaSpec(content, new[] {"all"});
        }

        protected NinjaSpec CreateWriteReadProject(string firstOutput, string secondOutput)
        {
            var content =
                $@"rule ruleA
    command = cmd /C ""echo r > $out""

rule ruleB
    command = cmd /C ""COPY $in {secondOutput}""

build {firstOutput}: ruleA
build {secondOutput} : ruleB {firstOutput}

build all: phony {secondOutput}
";
            return new NinjaSpec(content, new[] { "all" });
        }

        protected NinjaSpec CreateWriteReadDeleteProject(string firstOutput, string secondOutput)
        {
            var content =
                $@"rule ruleA
    command = cmd /C ""echo r > $out""

rule ruleB
    command = cmd /C ""COPY $in {secondOutput} && DEL /f $in""

build {firstOutput}: ruleA
build {secondOutput} : ruleB {firstOutput}

build all: phony {secondOutput}
";
            return new NinjaSpec(content, new[] { "all" });
        }

        protected NinjaSpec CreateProjectWithOrderOnlyDependencies(string firstOutput, string secondOutput)
        {
            var content =
                $@"rule ruleA
    command = cmd /C ""echo hola > {firstOutput}""

rule ruleB
    command = cmd /C ""COPY {firstOutput} $out""

build {firstOutput}: ruleA
build {secondOutput} : ruleB || {firstOutput}

build all: phony {secondOutput}
";
            return new NinjaSpec(content, new[] { "all" });
        }


        protected NinjaSpec CreateProjectThatCopiesResponseFile(string output, string responseFile, string responseFileContent)
        {
            var content =
                $@"rule copy_respfile
  command = cmd.exe /C ""COPY {responseFile} {output}""
  rspfile = {responseFile}
  rspfile_content = {responseFileContent}

build {output}: copy_respfile
build all: phony {output}
";
            return new NinjaSpec(content, new[] { "all" });
        }

        protected NinjaSpec CreateDummyFilePhonyProject(string phonyOutputFile1, string phonyOutputFile2, string dummyFile, string effectiveFile)
        {
            // Rule is built to mimic a Ninja install as generated by CMake
            var content = 
                $@"rule CUSTOM_COMMAND
  command = $COMMAND
  description = $DESC

rule WRITE_TEST
    command = cmd /C ""echo test > $out""

build {phonyOutputFile1}: WRITE_TEST

build {phonyOutputFile2}: WRITE_TEST

build all: phony {phonyOutputFile1} {phonyOutputFile2}

build {dummyFile}: CUSTOM_COMMAND all
  COMMAND = cmd.exe /C ""echo works > {effectiveFile}""
  DESC = Install the project...
  pool = console
  restat = 1

build install: phony {dummyFile}
";

            return new NinjaSpec(content, new[] { "install" });
        }

        private string NinjaPrelude(
            string projectRoot = null,
            string specFile = null,
            IEnumerable<string> targets = null,
            bool includeProjectRoot = true,
            bool includeSpecFile = true) => $@"
config({{
    disableDefaultSourceResolver: true,
    resolvers: [
        {{
            kind: 'Ninja',
            targets: [{ ExpandTargetsOrGetDefault(targets)} ],
            {(includeSpecFile ? "specFile: f`" + (specFile ?? DefaultSpecFileLocation) + "`," : "")}
            {(includeProjectRoot ? "projectRoot: d`" + (projectRoot ?? DefaultProjectRoot) + "`," : "")}
            moduleName: ""DefaultModule""
        }},
    ],
{MountsArray(projectRoot ?? DefaultProjectRoot)}
}});";
        private string MountsArray(string projectRoot) => projectRoot == null ? string.Empty : $@"mounts: [
        {{
            name: a`{projectRoot}`,
            path: p`{projectRoot}`,
            trackSourceFileChanges: true,
            isWritable: true,
            isReadable: true,
            isScrubbable: true
        }}
    ]";

        private string ExpandTargetsOrGetDefault(IEnumerable<string> targets) => targets != null ? string.Join(",", targets.Select(t => $"\"{t}\"")) : "\"all\"";
    }
}
