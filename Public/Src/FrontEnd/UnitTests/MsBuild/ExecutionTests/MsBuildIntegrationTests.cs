// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Engine.Tracing;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using Test.BuildXL.EngineTestUtilities;
using Xunit;
using Xunit.Abstractions;
using System;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;

namespace Test.BuildXL.FrontEnd.MsBuild
{
    /// <summary>
    /// Uses an MSBuild resolver to schedule and execute pips based on MSBuild.
    /// </summary>
    /// <remarks>
    /// These tests actually execute pips, and are therefore expensive
    /// </remarks>
    public class MsBuildIntegrationTests : MsBuildPipExecutionTestBase
    {
        public MsBuildIntegrationTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void EndToEndPipExecutionWithDependencies()
        {
            const string TestProj1 = "test1.csproj";
            var pathToTestProj1 = R("public", "dir1", TestProj1);
            
            const string TestProj2 = "test2.csproj";
            var pathToTestProj2 = R("public", "dir2", TestProj2);

            const string Dirs = "dirs.proj";
            var config = Build()
                    .AddSpec(Dirs, CreateDirsProject(pathToTestProj1, pathToTestProj2))
                    .AddSpec(pathToTestProj1, CreateWriteFileTestProject("MyFile1.txt"))
                    .AddSpec(pathToTestProj2, CreateWriteFileTestProject("MyFile2.txt", projectReference: pathToTestProj1))
                    .PersistSpecsAndGetConfiguration();

            var engineResult = RunEngineWithConfig(config);
            Assert.True(engineResult.IsSuccess);

            var pipGraph = engineResult.EngineState.PipGraph;

            // We should find three process pips: the two test projects and 'dirs'
            var processPips = pipGraph.RetrievePipsOfType(PipType.Process).ToList();
            Assert.Equal(3, processPips.Count);

            var testProj1 = processPips.Find(pip => pip.Provenance.OutputValueSymbol.ToString(engineResult.EngineState.SymbolTable).Contains(TestProj1));
            Assert.True(testProj1 != null);

            var testProj2 = processPips.Find(pip => pip.Provenance.OutputValueSymbol.ToString(engineResult.EngineState.SymbolTable).Contains(TestProj2));
            Assert.True(testProj2 != null);

            var dirs = processPips.Find(pip => pip.Provenance.OutputValueSymbol.ToString(engineResult.EngineState.SymbolTable).Contains(Dirs));
            Assert.True(dirs != null);

            // Let's do some basic graph validation
            // We should be able to reach the first test project from the second one
            Assert.True(pipGraph.DataflowGraph.IsReachableFrom(testProj1.PipId.ToNodeId(), testProj2.PipId.ToNodeId(), skipOutOfOrderNodes: true));
            // We should be able to reach both test projects from 'dirs' project
            Assert.True(pipGraph.DataflowGraph.IsReachableFrom(testProj1.PipId.ToNodeId(), dirs.PipId.ToNodeId(), skipOutOfOrderNodes: true));
            Assert.True(pipGraph.DataflowGraph.IsReachableFrom(testProj2.PipId.ToNodeId(), dirs.PipId.ToNodeId(), skipOutOfOrderNodes: true));

            // Make sure pips actually ran, and the files to be written are there
            Assert.True(File.Exists(Path.Combine(RelativeSourceRoot, "public", "dir1", OutDir, "MyFile1.txt")));
            Assert.True(File.Exists(Path.Combine(RelativeSourceRoot, "public", "dir2", OutDir, "MyFile2.txt")));
        }

        [Fact]
        public void MSBuildCacheGraphBehavior()
        {
            var testCache = new TestCache();

            const string TestProj1 = "test1.csproj";
            var pathToTestProj1 = R("public", "dir1", TestProj1);

            const string TestProj2 = "test2.csproj";
            var pathToTestProj2 = R("public", "dir2", TestProj2);

            const string Dirs = "dirs.proj";
            var config = (CommandLineConfiguration)Build($"fileNameEntryPoints: [ r`{Dirs}` ]")
                    .AddSpec(Dirs, CreateDirsProject(pathToTestProj1, pathToTestProj2))
                    .AddSpec(pathToTestProj1, CreateWriteFileTestProject("MyFile1.txt"))
                    .AddSpec(pathToTestProj2, CreateWriteFileTestProject("MyFile2.txt", projectReference: pathToTestProj1))
                    .PersistSpecsAndGetConfiguration();

            config.Cache.CacheGraph = true;
            config.Cache.AllowFetchingCachedGraphFromContentCache = true;
            config.Cache.Incremental = true;

            // First time the graph should be computed
            var engineResult = RunEngineWithConfig(config, testCache);
            Assert.True(engineResult.IsSuccess);
            
            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);
            AssertLogContains(false, "Storing pip graph descriptor to cache: Status: Success");

            // The second build should fetch and reuse the graph from the cache
            engineResult = RunEngineWithConfig(config, testCache);
            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void PassthroughVariablesAreHonored(bool isPassThrough)
        {
            var testCache = new TestCache();

            const string TestProj1 = "test1.csproj";
            var pathToTestProj1 = R("public", TestProj1);

            Environment.SetEnvironmentVariable("Test", "originalValue");

            var environment = new Dictionary<string, DiscriminatingUnion<string, UnitValue>> {
                ["Test"] = isPassThrough ? 
                    new DiscriminatingUnion<string, UnitValue>(UnitValue.Unit) :
                    new DiscriminatingUnion<string, UnitValue>(Environment.GetEnvironmentVariable("Test"))
            };

            var config = (CommandLineConfiguration)Build(
                            runInContainer: false, 
                            environment: environment, 
                            globalProperties: null,
                            filenameEntryPoint: pathToTestProj1)
                    .AddSpec(pathToTestProj1, CreateWriteFileTestProject("MyFile"))
                    .PersistSpecsAndGetConfiguration();

            config.Sandbox.FileSystemMode = FileSystemMode.RealAndMinimalPipGraph;

            config.Cache.CacheGraph = true;
            config.Cache.AllowFetchingCachedGraphFromContentCache = true;
            config.Cache.Incremental = true;

            // First time the graph should be computed
            var engineResult = RunEngineWithConfig(config, testCache);
            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);
            AssertLogContains(false, "Storing pip graph descriptor to cache: Status: Success");
            
            Environment.SetEnvironmentVariable("Test", "modifiedValue");

            engineResult = RunEngineWithConfig(config, testCache);
            Assert.True(engineResult.IsSuccess);

            // If the variable is a passthrough, the change shouldn't affect caching
            if (isPassThrough)
            {
                AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues, count: 0);
                AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);
            }
            else
            {
                AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues);
                AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);
            }
        }

        [Fact]
        public void ProjectThatRemovesAFileDuringEvaluationIsProperlyHandled()
        {
            // This project writes and deletes a file during evaluation
            var mainProject =
@"<Project>
    <PropertyGroup>
        <AddAndRemoveFile>
            $([System.IO.File]::WriteAllText('temp.txt', 'hello'))$([System.IO.File]::Delete('temp.txt'))
        </AddAndRemoveFile>
    </PropertyGroup>
    <Target Name = 'Build'/>
 </Project>";

            var config = Build(environment: new Dictionary<string, string> { ["MSBUILDENABLEALLPROPERTYFUNCTIONS"] = "1" })
                .AddSpec(R("MainProject.proj"), mainProject)
                .PersistSpecsAndGetConfiguration();

            // Even though there will be an access to a file that is not there anymore, we should run to success
            var result = RunEngineWithConfig(config);
            Assert.True(result.IsSuccess);
        }
    }
}
