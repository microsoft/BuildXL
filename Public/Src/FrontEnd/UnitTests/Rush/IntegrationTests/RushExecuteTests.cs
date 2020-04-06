// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using BuildXL.Engine;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.FrontEnd.Rush.IntegrationTests;
using Xunit;
using Xunit.Abstractions;
using LogEventId = global::BuildXL.FrontEnd.Rush.Tracing.LogEventId;

namespace Test.BuildXL.FrontEnd.Rush
{
    public class RushExecuteTests : RushIntegrationTestBase
    {
        public RushExecuteTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // We don't actually need to execute anything, scheduling is enough
        protected override EnginePhases Phase => EnginePhases.Schedule;

        [Theory]
        [InlineData("['build', 'build']", LogEventId.RushCommandIsDuplicated)]
        [InlineData("['']", LogEventId.RushCommandIsEmpty)]
        [InlineData("[{command: 'build', dependsOn: [{kind: 'local', command: 'build'}]}]", LogEventId.CycleInRushCommands)]
        [InlineData(@"[{command: 'build', dependsOn: [{kind: 'local', command: 'test'}]}, 
                       {command: 'test', dependsOn: [{kind: 'local', command: 'build'}]}]", LogEventId.CycleInRushCommands)]
        public void InvalidRushCommands(string commands, LogEventId expectedLogEventId)
        {
            var engineResult = BuildDummyWithCommands(commands);
            Assert.False(engineResult.IsSuccess);

            AssertErrorEventLogged(expectedLogEventId);
        }

        [Fact]
        public void DefaultCommandIsBuild()
        {
            var engineResult = BuildDummyWithCommands(commands: null);
            Assert.True(engineResult.IsSuccess);
            Assert.Equal(1, engineResult.EngineState.PipGraph.RetrievePipReferencesOfType(global::BuildXL.Pips.Operations.PipType.Process).Count());
        }

        [Fact]
        public void SimpleCommandsLocalDependencyBehavior()
        {
            // Schedule a single project with three script commands
            // The script commands themselves are not important since nothing gets executed, just scheduled

            var config = Build(executeCommands: "['build', 'sign', 'test']")
                .AddRushProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "build A"), ("sign", "sign A"), ("test", "test A") })
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.True(result.IsSuccess);

            // We should be able to retrieve 3 pips
            var pipBuild = result.EngineState.RetrieveProcess("@ms/project-A", "build");
            var pipSign = result.EngineState.RetrieveProcess("@ms/project-A", "sign");
            var pipTest = result.EngineState.RetrieveProcess("@ms/project-A", "test");

            // Their dependencies should be build <- sign <- test
            Assert.True(IsDependencyAndDependent(pipBuild, pipSign));
            Assert.True(IsDependencyAndDependent(pipSign, pipTest));
        }

        [Fact]
        public void SimpleCommandsDirectDependencyBehavior()
        {
            // Schedule two projects, where B depends on A and requests 'build' and 'test' to be executed
            // The script commands themselves are not important since nothing gets executed, just scheduled
            // We should find B build depending on A build, and B test depending on B build
            var config = Build(executeCommands: "['build', 'test']")
                .AddRushProject("@ms/project-A", "src/A", 
                    scriptCommands: new[] { ("build", "build A"), ("test", "test A") })
                .AddRushProject("@ms/project-B", "src/B", dependencies: new[] { "@ms/project-A" }, 
                    scriptCommands: new[] { ("build", "build B"), ("test", "test B") })
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
                ("src/B", "@ms/project-B")
            });

            Assert.True(result.IsSuccess);

            var pipABuild = result.EngineState.RetrieveProcess("@ms/project-A", "build");
            var pipATest = result.EngineState.RetrieveProcess("@ms/project-A", "test");
            var pipBBuild = result.EngineState.RetrieveProcess("@ms/project-B", "build");
            var pipBTest = result.EngineState.RetrieveProcess("@ms/project-B", "test");

            // Their dependencies should be:
            // A build <- B build <- B test, A build <- A test and B build <- B test
            // There shouldn't be a dependency A test <- B test
            Assert.True(IsDependencyAndDependent(pipABuild, pipBBuild));
            Assert.True(IsDependencyAndDependent(pipABuild, pipATest));
            Assert.True(IsDependencyAndDependent(pipABuild, pipBBuild));
            Assert.True(IsDependencyAndDependent(pipBBuild, pipBTest));
            Assert.False(IsDependencyAndDependent(pipATest, pipBTest));
        }

        [Fact]
        public void FullCommandsDependencyBehavior()
        {
            // Create three projects such that A <- B and A <- C
            // Define (somewhat arbitrary) local and package dependencies for pre-build, build and post-build scripts
            // All three projects have the 3 scripts available, but A, which doesn't have post-build

            var commands = @"
[
    {command: 'build', dependsOn: [{kind: 'local', command: 'pre-build'}, {kind: 'package', command: 'build'}]},
    {command: 'pre-build', dependsOn: []},
    {command: 'post-build', dependsOn: [{kind: 'package', command: 'post-build'}]}
]";

            var config = Build(executeCommands: commands)
                .AddRushProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "b A"), ("pre-build", "pr A") })
                .AddRushProject("@ms/project-B", "src/B", scriptCommands: new[] { ("build", "b B"), ("pre-build", "pr B"), ("post-build", "ps B") },
                    dependencies: new[] { "@ms/project-A" })
                .AddRushProject("@ms/project-C", "src/C", scriptCommands: new[] { ("build", "b C"), ("pre-build", "pr C"), ("post-build", "ps C") },
                    dependencies: new[] { "@ms/project-A" })
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
                ("src/B", "@ms/project-B"),
                ("src/C", "@ms/project-C")
            });

            Assert.True(result.IsSuccess);

            // We are expecting these relationships

            // A pre-build  <-  A build     A post-build(missing)
            //                     ^    ^         ^       ^
            //                     |    |         |       |
            // B pre-build  <-  B build |    B post-build |
            //                          |                 |
            //                          |                 |
            // C pre-build  <-  C build-|    C post-build-|

            // We should be able to retrieve 8 pips (A post-build missing)
            var aPreBuild = result.EngineState.RetrieveProcess("@ms/project-A", "pre-build");
            var bPreBuild = result.EngineState.RetrieveProcess("@ms/project-B", "pre-build");
            var cPreBuild = result.EngineState.RetrieveProcess("@ms/project-C", "pre-build");

            var aBuild = result.EngineState.RetrieveProcess("@ms/project-A", "build");
            var bBuild = result.EngineState.RetrieveProcess("@ms/project-B", "build");
            var cBuild = result.EngineState.RetrieveProcess("@ms/project-C", "build");

            var aPostBuild = result.EngineState.RetrieveProcess("@ms/project-A", "post-build");
            var bPostBuild = result.EngineState.RetrieveProcess("@ms/project-B", "post-build");
            var cPostBuild = result.EngineState.RetrieveProcess("@ms/project-C", "post-build");

            // A post build should not exist. The other post builds should.
            Assert.True(aPostBuild == null);
            Assert.True(bPostBuild != null);
            Assert.True(cPostBuild != null);

            // Script A post-build should cause two dependencies (going from B and C post-build) ignored
            AssertInformationalEventLogged(LogEventId.DependencyIsIgnoredScriptIsMissing, count: 2);
            AssertInformationalEventLogged(LogEventId.ProjectIsIgnoredScriptIsMissing);

            // Now check dependencies
            Assert.True(IsDependencyAndDependent(aPreBuild, aBuild));
            Assert.True(IsDependencyAndDependent(bPreBuild, bBuild));
            Assert.True(IsDependencyAndDependent(cPreBuild, cBuild));
            Assert.True(IsDependencyAndDependent(aBuild, bBuild));
            Assert.True(IsDependencyAndDependent(aBuild, cBuild));

            // And check (some) absent ones
            Assert.False(IsDependencyAndDependent(bBuild, bPostBuild));
            Assert.False(IsDependencyAndDependent(aPreBuild, bPreBuild));
        }

        private BuildXLEngineResult BuildDummyWithCommands(string commands)
        {
            var config = Build(executeCommands: commands)
                    .AddRushProject("@ms/project-A", "src/A")
                    .PersistSpecsAndGetConfiguration();

            return RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });
        }
    }
}
