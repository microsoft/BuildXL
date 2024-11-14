// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Engine.Tracing;
using BuildXL.Native.IO;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.EngineTestUtilities;
using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Rush
{
    [Trait("Category", "RushIntegrationTests")]
    public class RushIntegrationTests : RushIntegrationTestBase
    {
        public RushIntegrationTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void JavaScriptWarningRegexTest()
        {
            var config = Build(warningRegex: "'WARNING'")
                .AddJavaScriptProject("@ms/project-A", "src/A", "console.log('This is a WARNING message!')")
                .AddJavaScriptProject("@ms/project-B", "src/B", "console.log('This is a regular message!')")
                .PersistSpecsAndGetConfiguration();

            var engineResult = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
                ("src/B", "@ms/project-B"),
            });

            Assert.True(engineResult.IsSuccess);

            // Let's do some basic graph validations
            var processes = engineResult.EngineState.PipGraph.RetrievePipsOfType(global::BuildXL.Pips.Operations.PipType.Process).ToList();
            // There should be two process pips
            Assert.Equal(2, processes.Count);
            // A PipProcessWarning should be logged
            AssertWarningEventLogged(global::BuildXL.Processes.Tracing.LogEventId.PipProcessWarning, 1);
        }

        [Fact]
        public void JavaScriptErrorRegexTest()
        {
            var content = @"
                function createError(x) {
                    if (x == 1) {
                        throw new Error('This is an error message');
                    }
                }
                createError(1);
            ";

            // Create a failed JavaScript project
            var config = Build(errorRegex: "'message'")
                .AddJavaScriptProject("@ms/project-A", "src/A", content)
                .PersistSpecsAndGetConfiguration();

            var engineResult = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
            });

            Assert.False(engineResult.IsSuccess);

            // Let's do some basic graph validations
            var processes = engineResult.EngineState.PipGraph.RetrievePipsOfType(global::BuildXL.Pips.Operations.PipType.Process).ToList();
            // There should be one process pips
            Assert.Equal(1, processes.Count);

            // A PipProcessError should be logged
            AssertErrorEventLogged(global::BuildXL.Processes.Tracing.LogEventId.PipProcessError, 1);
            // Error message has been filtered by a regex, it should only contains the line contains 'error';
            AssertLogNotContains(true, "createError(1);");
            AssertLogContains(true, "This is an error message");
        }

        [Fact]
        public void JavaScriptPipTimeoutErrorForProject()
        {
            var config = Build(timeouts: "[{timeout: '500ms', projectSelector: ['@ms/project-B']}]")
                .AddJavaScriptProject("@ms/project-A", "src/A", "setTimeout(function(){console.log('sleep over!')}, 1000)")
                .AddJavaScriptProject("@ms/project-B", "src/B", "setTimeout(function(){console.log('sleep over!')}, 1000)")
                .PersistSpecsAndGetConfiguration();

            var engineResult = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
                ("src/B", "@ms/project-B"),
            });

            Assert.False(engineResult.IsSuccess);

            // Let's do some basic graph validations
            var processes = engineResult.EngineState.PipGraph.RetrievePipsOfType(global::BuildXL.Pips.Operations.PipType.Process).ToList();
            // There should be two process pips
            Assert.Equal(2, processes.Count);
            // A PipProcessTookTooLongError should be logged
            AssertErrorEventLogged(global::BuildXL.Processes.Tracing.LogEventId.PipProcessTookTooLongError, 1);
        }

        [Fact]
        public void JavaScriptPipTimeoutWarningForProject()
        {
            var config = Build(timeouts: "[{warningTimeout: '500', projectSelector: ['@ms/project-B']}]")
                .AddJavaScriptProject("@ms/project-A", "src/A", "setTimeout(function(){console.log('sleep over!')}, 1000)")
                .AddJavaScriptProject("@ms/project-B", "src/B", "setTimeout(function(){console.log('sleep over!')}, 1000)")
                .PersistSpecsAndGetConfiguration();

            var engineResult = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
                ("src/B", "@ms/project-B"),
            });

            Assert.True(engineResult.IsSuccess);

            // Let's do some basic graph validations
            var processes = engineResult.EngineState.PipGraph.RetrievePipsOfType(global::BuildXL.Pips.Operations.PipType.Process).ToList();
            // There should be two process pips
            Assert.Equal(2, processes.Count);
            // A PipProcessTookTooLongWarning should be logged
            AssertWarningEventLogged(global::BuildXL.Processes.Tracing.LogEventId.PipProcessTookTooLongWarning, 1);
        }

        [Fact]
        public void JavaScriptPipTimeoutForDifferentProjects()
        {
            var config = Build(timeouts: "[{warningTimeout: '500', projectSelector: ['@ms/project-B', '@ms/project-C']}, {warningTimeout: '500ms', projectSelector: ['@ms/project-D']}, {timeout: '500', projectSelector: ['@ms/project-E']}]")
                .AddJavaScriptProject("@ms/project-A", "src/A", "setTimeout(function(){console.log('sleep over!')}, 1000)")
                .AddJavaScriptProject("@ms/project-B", "src/B", "setTimeout(function(){console.log('sleep over!')}, 1000)")
                .AddJavaScriptProject("@ms/project-C", "src/C", "setTimeout(function(){console.log('sleep over!')}, 1000)")
                .AddJavaScriptProject("@ms/project-D", "src/D", "setTimeout(function(){console.log('sleep over!')}, 1000)")
                .AddJavaScriptProject("@ms/project-E", "src/E", "setTimeout(function(){console.log('sleep over!')}, 1000)")
                .PersistSpecsAndGetConfiguration();

            var engineResult = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
                ("src/B", "@ms/project-B"),
                ("src/C", "@ms/project-C"),
                ("src/D", "@ms/project-D"),
                ("src/E", "@ms/project-E"),
            });

            Assert.False(engineResult.IsSuccess);
            // Let's do some basic graph validations
            var processes = engineResult.EngineState.PipGraph.RetrievePipsOfType(global::BuildXL.Pips.Operations.PipType.Process).ToList();
            // There should be two process pips
            Assert.Equal(5, processes.Count);

            // There should be 3 PipProcessTookTooLongWarning logged
            AssertWarningEventLogged(global::BuildXL.Processes.Tracing.LogEventId.PipProcessTookTooLongWarning, 3);
            // There should be 1 PipProcessTookTooLongError logged
            AssertErrorEventLogged(global::BuildXL.Processes.Tracing.LogEventId.PipProcessTookTooLongError, 1);
        }

        [Theory]
        [InlineData("233a")]
        [InlineData("233mm")]
        [InlineData("233msh")]
        public void JavaScriptInvalidPipTimeout(string timeoutDuration)
        {
            var config = Build(timeouts: $"[{{timeout: '{timeoutDuration}', projectSelector: ['@ms/project-B']}}]")
                .AddJavaScriptProject("@ms/project-A", "src/A", "setTimeout(function(){console.log('sleep over!')}, 1000)")
                .AddJavaScriptProject("@ms/project-B", "src/B", "setTimeout(function(){console.log('sleep over!')}, 1000)")
                .PersistSpecsAndGetConfiguration();

            var engineResult = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
                ("src/B", "@ms/project-B"),
            });

            Assert.False(engineResult.IsSuccess);

            // A PipProcessTookTooLongError should be logged
            AssertErrorEventLogged(global::BuildXL.FrontEnd.JavaScript.Tracing.LogEventId.InvalidTimeoutDurationValue, 1);
        }

        [Fact]
        public void EndToEndPipExecutionWithDependencies()
        {
            // Create two projects A and B such that A -> B.
            var config = Build()
                .AddJavaScriptProject("@ms/project-A", "src/A", "module.exports = function A(){}")
                .AddJavaScriptProject("@ms/project-B", "src/B", "const A = require('@ms/project-A'); return A();", new string[] { "@ms/project-A"})
                .PersistSpecsAndGetConfiguration();

            var engineResult = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
                ("src/B", "@ms/project-B"),
            });

            Assert.True(engineResult.IsSuccess);
            
            // Let's do some basic graph validations
            var processes = engineResult.EngineState.PipGraph.RetrievePipsOfType(global::BuildXL.Pips.Operations.PipType.Process).ToList();
            // There should be two process pips
            Assert.Equal(2, processes.Count);

            // Project A depends on project B
            var projectAPip = processes.First(pip => ((Process)pip).Provenance.OutputValueSymbol.ToString(engineResult.EngineState.SymbolTable).Contains("project_A"));
            var projectBPip = processes.First(pip => ((Process)pip).Provenance.OutputValueSymbol.ToString(engineResult.EngineState.SymbolTable).Contains("project_B"));
            Assert.True(engineResult.EngineState.PipGraph.IsReachableFrom(projectAPip.PipId.ToNodeId(), projectBPip.PipId.ToNodeId()));
        }

        [Fact]
        public void RushCacheGraphBehavior()
        {
            var testCache = new TestCache();

            var config = (CommandLineConfiguration)Build()
                    .AddJavaScriptProject("@ms/project-A", "src/A")
                    .PersistSpecsAndGetConfiguration();

            config.Cache.CacheGraph = true;
            config.Cache.AllowFetchingCachedGraphFromContentCache = true;
            config.Cache.Incremental = true;

            // First time the graph should be computed
            var engineResult = RunRushProjects(
                config, 
                new[] {("src/A", "@ms/project-A") },
                testCache);

            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);
            AssertLogContains(false, "Storing pip graph descriptor to cache: Status: Success");

            // The second build should fetch and reuse the graph from the cache
            engineResult = RunRushProjects(
                config, 
                new[] {("src/A", "@ms/project-A")},
                testCache);

            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);
        }

        [Fact]
        public void ExposedEnvironmentVariablesBehavior()
        {
            var testCache = new TestCache();

            // Set env var 'Test' to an arbitrary value, but override that value in the main config, so
            // we actually don't depend on it
            Environment.SetEnvironmentVariable("Test", "2");

            var environment = new Dictionary<string, string>
            {
                ["PATH"] = PathToNodeFolder,
                ["Test"] = "3"
            };

            var config = (CommandLineConfiguration)Build(environment)
                    .AddJavaScriptProject("@ms/project-A", "src/A")
                    .PersistSpecsAndGetConfiguration();

            config.Cache.CacheGraph = true;
            config.Cache.AllowFetchingCachedGraphFromContentCache = true;
            config.Cache.Incremental = true;

            // First time the graph should be computed
            var engineResult = RunRushProjects(
                config,
                new[] { ("src/A", "@ms/project-A") },
                testCache);
            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);
            AssertLogContains(false, "Storing pip graph descriptor to cache: Status: Success");

            // Change the environment variable. Since we overrode the value, the second
            // build should fetch and reuse the graph from the cache
            Environment.SetEnvironmentVariable("Test", "3");
            engineResult = RunRushProjects(
                config,
                new[] { ("src/A", "@ms/project-A") },
                testCache);
            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);
        }

        [Fact]
        public void PassthroughVariablesAreHonored()
        {
            var testCache = new TestCache();

            Environment.SetEnvironmentVariable("Test", "originalValue");

            var environment = new Dictionary<string, DiscriminatingUnion<string, UnitValue>> { 
                ["PATH"] = new DiscriminatingUnion<string, UnitValue>(PathToNodeFolder), 
                ["Test"] = new DiscriminatingUnion<string, UnitValue>(UnitValue.Unit) };

            var config = (CommandLineConfiguration)Build(environment)
                .AddJavaScriptProject("@ms/project-A", "src/A")
                .PersistSpecsAndGetConfiguration();

            config.Cache.CacheGraph = true;
            config.Cache.AllowFetchingCachedGraphFromContentCache = true;
            config.Cache.Incremental = true;

            // First time the graph should be computed
            var engineResult = RunRushProjects(
                config,
                new[] { ("src/A", "@ms/project-A") },
                testCache);

            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);
            AssertLogContains(false, "Storing pip graph descriptor to cache: Status: Success");

            Environment.SetEnvironmentVariable("Test", "modifiedValue");

            engineResult = RunRushProjects(
                config,
                new[] { ("src/A", "@ms/project-A") },
                testCache);

            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);
        }

        [Fact]
        public void DuplicateProjectNamesAreBlocked()
        {
            // Create two projects with the same package name
            var config = Build()
                    .AddJavaScriptProject("@ms/project-A", "src/A")
                    .AddJavaScriptProject("@ms/project-A", "src/B")
                    .PersistSpecsAndGetConfiguration();

            bool exceptionOccured = false;

            try
            {
                var engineResult = RunRushProjects(config, new[] {
                    ("src/A", "@ms/project-A"),
                    ("src/B", "@ms/project-A"),
                });
            }
            catch (InvalidOperationException e)
            {
                exceptionOccured = true;
                Assert.Equal("Rush update failed.", e.Message);
            }

            Assert.True(exceptionOccured, "Rush update should have failed due to Duplicate Project Names but didn't");
        }

        [Fact]
        public void IntermediateDirectoryCasingIsPreserved()
        {
            var testCache = new TestCache();

            // Run a project that writes a file under a nested directory
            var config = (CommandLineConfiguration)Build()
                    .AddJavaScriptProjectWithExplicitVersions(
                        "@ms/project-A", 
                        "src/A", 
                        "var fs = require('fs'); fs.mkdirSync('CamelCasedLib'); fs.writeFileSync('CamelCasedLib/out.txt', 'hello');",
                        new[] { ("path", "0.12.7") })
                    .PersistSpecsAndGetConfiguration();

            config.Cache.CacheGraph = true;
            config.Cache.AllowFetchingCachedGraphFromContentCache = true;
            config.Cache.Incremental = true;

            var engineResult = RunRushProjects(
                config,
                new[] { ("src/A", "@ms/project-A") },
                testCache);

            // Make sure the directory was written with the expected case (via a case sensitive comparison)
            Assert.True(engineResult.IsSuccess);
            string intermediateDir = Directory.EnumerateDirectories(Path.Combine(SourceRoot, "src", "A"))
                .First(dir => dir.EndsWith("CamelCasedLib", StringComparison.Ordinal));
            Assert.NotNull(intermediateDir);

            // Delete the created directory and run from cache
            FileUtilities.DeleteDirectoryContents(intermediateDir, deleteRootDirectory: true);

            engineResult = RunRushProjects(
                config,
                new[] { ("src/A", "@ms/project-A") },
                testCache);

            // The result should have preserved casing
            Assert.True(engineResult.IsSuccess);
            intermediateDir = Directory.EnumerateDirectories(Path.Combine(SourceRoot, "src", "A"))
                .First(dir => dir.EndsWith("CamelCasedLib", StringComparison.Ordinal));
            Assert.NotNull(intermediateDir);
        }

        [Fact]
        public void CallbackIsHonored()
        {
            // Create two JS projects such that A <- B
            // Configure a custom scheduler that customizes only A to produce a file in the root of the project folder. B is scheduled as usual
            var config =
                Build(
                    schedulingCallback: "{module: 'myModule', schedulingFunction: 'Test.custom'}",
                    addDScriptResolver: true)
               .AddJavaScriptProject("@ms/project-A", "src/A")
               .AddJavaScriptProject("@ms/project-B", "src/B", dependencies: new[] { "@ms/project-A" }, scriptCommands: new[] { ("build", "echo HelloB") })
               .AddSpec("module.config.dsc", "module({name: 'myModule'});")
               .AddSpec(@"
import {Transformer, Artifact, Cmd} from 'Sdk.Transformers';

const tool : Transformer.ToolDefinition = { 
    exe: Context.isWindowsOS()? Environment.getFileValue('COMSPEC') : f`/usr/bin/bash`,
    dependsOnCurrentHostOSDirectories: true
};

namespace Test {
    @@public
    export function custom(project : JavaScriptProject) : Transformer.ExecuteResult {
        if (project.name === '@ms/project-A') {
            return Transformer.execute({
                workingDirectory: project.projectFolder,
                arguments: [
                    Cmd.argument(Context.isWindowsOS() ? ""/C"" : ""-c""),
                    Cmd.rawArgument('""'),
                    Cmd.args([""echo"", ""HelloA"", "">"", ""file.txt""]),
                    Cmd.rawArgument('""')
                ],
                tool: tool,
                outputs: project.outputs.filter(output => typeof output !== 'Path').map(output => <Transformer.DirectoryOutput>{ directory: output, kind: 'shared' })
            });
        }
        else {
            return undefined;
        }
    }
}")
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
                ("src/B", "@ms/project-B")
            });

            Assert.True(result.IsSuccess);

            var processA = result.EngineState.RetrieveProcess("@ms/project-A");
            var processB = result.EngineState.RetrieveProcess("@ms/project-B");

            // Let's make sure A was scheduled by the callback and B followed the regular scheduling
            XAssert.Contains(processA.Arguments.ToString(PathTable), "HelloA");
            XAssert.Contains(processB.Arguments.ToString(PathTable), "HelloB");

            // The output file should have been produced by A
            XAssert.IsTrue(File.Exists(config.Layout.SourceDirectory.Combine(PathTable, RelativePath.Create(StringTable, "src/A/file.txt")).ToString(PathTable)));
        }
    }
}
