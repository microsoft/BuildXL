// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Engine;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.EngineTestUtilities;
using Test.BuildXL.TestUtilities;
using Test.DScript.Ast;
using Test.BuildXL.FrontEnd.Ninja.Infrastructure;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;
using Test.BuildXL.Processes;
using BuildXL.Utilities;
using System;

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

        protected string SHELL => OperatingSystemHelper.IsWindowsOS ? $@"{CmdHelper.CmdX64} /C" : $@"{CmdHelper.Bash} -c";
        protected string COPY => OperatingSystemHelper.IsWindowsOS ? "COPY" : "/usr/bin/cp";
        protected string DELETE => OperatingSystemHelper.IsWindowsOS ? "DEL /f" : "/usr/bin/rm -f";

        /// <summary>
        /// Root to the source enlistment root
        /// </summary>
        protected string SourceRoot { get; }

        // By default the engine runs e2e
        protected virtual EnginePhases Phase => EnginePhases.Execute;

        protected override bool DisableDefaultSourceResolver => true;

        private string GetOSBasedEnvString(string envName)
        {
            return OperatingSystemHelper.IsWindowsOS ? $@"%{envName}%" : $@"$${envName}";
        }

        protected NinjaPipExecutionTestBase(ITestOutputHelper output) : base(output, true)
        {
            RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Pips.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Core.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Script.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Nuget.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Ninja.ETWLogger.Log);
            
            SourceRoot = Path.Combine(TestRoot, RelativeSourceRoot);
        }

        /// <inheritdoc/>
        protected ICommandLineConfiguration BuildAndGetConfiguration(NinjaSpec spec,
            bool includeProjectRoot = true,
            bool includeSpecFile = true,
            IEnumerable<(string Key, string Value)> environment = null,
            IEnumerable<string> passthroughs = null,
            IEnumerable<string> additionalOutputDirectories = null)
        {
            var environmentDict = (environment == null && passthroughs == null) ? null : new Dictionary<string, DiscriminatingUnion<string, UnitValue>>();

            if (environment != null) 
            { 
                foreach (var (k, v) in environment)
                {
                    environmentDict.Add(k, new DiscriminatingUnion<string, UnitValue>(v));
                }
            }

            if (passthroughs != null)
            {
                foreach (var p in passthroughs)
                {
                    environmentDict.Add(p, new DiscriminatingUnion<string, UnitValue>(UnitValue.Unit));
                }
            }

            string additionalDirs = null;
            if (additionalOutputDirectories != null)
            {
                additionalDirs = $"[{string.Join(",", additionalOutputDirectories)}]";
            }

            return base.Build()
                .Configuration(NinjaPrelude(targets: spec.Targets, includeProjectRoot: includeProjectRoot, includeSpecFile: includeSpecFile, environment: environmentDict, additionalOutputDirectories: additionalDirs))
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
                // Source resolver not properly registered for Ninja tests
                ((CommandLineConfiguration)config).DisableInBoxSdkSourceResolver = true;

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
    command = {SHELL} ""echo Hello World > $out""
build {outputFileName}: r
build all: phony {outputFileName}
";
            return new NinjaSpec(content, new[] {"all"});
        }

        /// <summary>
        /// Returns a project that echoes the value of an environment variable to an output file
        /// </summary>
        protected NinjaSpec CreatePrintEnvVariableProject(string outputFileName, string varName)
        {
            var content =
$@"rule r
    command = {SHELL} ""echo {GetOSBasedEnvString(varName)} > $out""
build {outputFileName}: r
build all: phony {outputFileName}
";
            return new NinjaSpec(content, new[] { "all" });
        }

        protected NinjaSpec CreateWriteReadProject(string firstOutput, string secondOutput)
        {
            var content =
$@"rule ruleA
    command = {SHELL} ""echo r > $out""

rule ruleB
    command = {SHELL} ""{COPY} $in {secondOutput}""

build {firstOutput}: ruleA
build {secondOutput}: ruleB {firstOutput}

build all: phony {secondOutput}
";
            return new NinjaSpec(content, new[] { "all" });
        }

        protected NinjaSpec CreateWriteReadDeleteProject(string firstOutput, string secondOutput)
        {
            var content =
$@"rule ruleA
    command = {SHELL} ""echo r > $out""

rule ruleB
    command = {SHELL} ""{COPY} $in {secondOutput} && {DELETE} $in""

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
    command = {SHELL} ""echo hola > {firstOutput}""

rule ruleB
    command = {SHELL} ""{COPY} {firstOutput} $out""

build {firstOutput}: ruleA
build {secondOutput}: ruleB || {firstOutput}

build all: phony {secondOutput}
";
            return new NinjaSpec(content, new[] { "all" });
        }

        protected NinjaSpec CreateProjectWithExtraneousWrite(string outputUnderCone, string secondOutputUnderCone, string outputPathOutsideOfCone)
        {
            var content =
$@"rule ruleA
    command = {SHELL} ""echo foo > {outputPathOutsideOfCone} && echo bar > $out""

rule ruleB
    command = {SHELL} ""{COPY} $in {secondOutputUnderCone}""

build {outputUnderCone}: ruleA
build {secondOutputUnderCone} : ruleB {outputUnderCone}

build all: phony {secondOutputUnderCone}
";
            return new NinjaSpec(content, new[] { "all" });
        }

        protected NinjaSpec CreateProjectThatCopiesResponseFile(string output, string responseFile, string responseFileContent)
        {
            var content =
$@"rule copy_respfile
  command = {SHELL} ""{COPY} {responseFile} {output}""
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
    command = {SHELL} ""echo test > $out""

build {phonyOutputFile1}: WRITE_TEST

build {phonyOutputFile2}: WRITE_TEST

build all: phony {phonyOutputFile1} {phonyOutputFile2}

build {dummyFile}: CUSTOM_COMMAND all
  COMMAND = {SHELL} ""echo works > {effectiveFile}""
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
            bool includeSpecFile = true,
            Dictionary<string, DiscriminatingUnion<string, UnitValue>> environment = null,
            string additionalOutputDirectories = null) => $@"
config({{
    resolvers: [
        {{
            kind: 'Ninja',
            targets: [{ ExpandTargetsOrGetDefault(targets)} ],
            {(includeSpecFile ? "specFile: f`" + (specFile ?? DefaultSpecFileLocation) + "`," : "")}
            {(includeProjectRoot ? "root: d`" + (projectRoot ?? DefaultProjectRoot) + "`," : "")}
            {(additionalOutputDirectories != null ? $"additionalOutputDirectories: {additionalOutputDirectories}," : string.Empty)}
            {DictionaryToExpression("environment", environment)}
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

        private static string DictionaryToExpression(string memberName, Dictionary<string, DiscriminatingUnion<string, UnitValue>> dictionary)
        {
            return (dictionary == null ?
                string.Empty :
                $"{memberName}: Map.empty<string, (PassthroughEnvironmentVariable | string)>(){ string.Join(string.Empty, dictionary.Select(property => $".add('{property.Key}', {(property.Value?.GetValue() is UnitValue ? "Unit.unit()" : $"'{property.Value?.GetValue()}'")})")) },");
        }
    }
}
