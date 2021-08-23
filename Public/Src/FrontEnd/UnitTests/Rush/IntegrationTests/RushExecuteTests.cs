// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using BuildXL.Engine;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;
using LogEventId = global::BuildXL.FrontEnd.JavaScript.Tracing.LogEventId;

namespace Test.BuildXL.FrontEnd.Rush
{
    [Trait("Category", "RushExecuteTests")]
    public class RushExecuteTests : RushIntegrationTestBase
    {
        public RushExecuteTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // We don't actually need to execute anything, scheduling is enough
        protected override EnginePhases Phase => EnginePhases.Schedule;

        [Theory]
        [InlineData("['build', 'build']", LogEventId.JavaScriptCommandIsDuplicated)]
        [InlineData("['']", LogEventId.JavaScriptCommandIsEmpty)]
        [InlineData("[{command: 'build', dependsOn: [{kind: 'local', command: 'build'}]}]", LogEventId.CycleInJavaScriptCommands)]
        [InlineData(@"[{command: 'build', dependsOn: [{kind: 'local', command: 'test'}]}, 
                       {command: 'test', dependsOn: [{kind: 'local', command: 'build'}]}]", LogEventId.CycleInJavaScriptCommands)]
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
                .AddJavaScriptProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "build A"), ("sign", "sign A"), ("test", "test A") })
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
                .AddJavaScriptProject("@ms/project-A", "src/A", 
                    scriptCommands: new[] { ("build", "build A"), ("test", "test A") })
                .AddJavaScriptProject("@ms/project-B", "src/B", dependencies: new[] { "@ms/project-A" }, 
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
                .AddJavaScriptProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "b A"), ("pre-build", "pr A") })
                .AddJavaScriptProject("@ms/project-B", "src/B", scriptCommands: new[] { ("build", "b B"), ("pre-build", "pr B"), ("post-build", "ps B") },
                    dependencies: new[] { "@ms/project-A" })
                .AddJavaScriptProject("@ms/project-C", "src/C", scriptCommands: new[] { ("build", "b C"), ("pre-build", "pr C"), ("post-build", "ps C") },
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

        [Fact]
        public void AbsentCommandForLocalDependency()
        {
            // Create a dependency tree such that D -> C -> B
            //                                         | -> E -> A
            var commands = @"
[
    {command: 'A', dependsOn: []},
    {command: 'B', dependsOn: []},
    {command: 'C', dependsOn: [{kind: 'local', command: 'B'}, {kind: 'local', command: 'E'}]},
    {command: 'D', dependsOn: [{kind: 'local', command: 'C'}]},
    {command: 'E', dependsOn: [{kind: 'local', command: 'A'}]},
]";

            // Create a project A where scripts C and E are missing
            var config = Build(executeCommands: commands)
                .AddJavaScriptProject("@ms/project-A", "src/A", scriptCommands: new[] { ("A", "script A"), ("B", "script B"), ("D", "script D") })
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
            });

            Assert.True(result.IsSuccess);

            var a = result.EngineState.RetrieveProcess("@ms/project-A", "A");
            var b = result.EngineState.RetrieveProcess("@ms/project-A", "B");
            var c = result.EngineState.RetrieveProcess("@ms/project-A", "C");
            var d = result.EngineState.RetrieveProcess("@ms/project-A", "D");
            var e = result.EngineState.RetrieveProcess("@ms/project-A", "E");

            Assert.True(a != null);
            Assert.True(b != null);
            Assert.True(c == null);
            Assert.True(d != null);
            Assert.True(e == null);

            // Now check dependencies. Since C and E are not available, we shoud get
            // D -> A
            // | -> B

            Assert.True(IsDependencyAndDependent(a, d));
            Assert.True(IsDependencyAndDependent(b, d));
        }

        [Fact]
        public void AbsentCommandForPackageDependency()
        {
            // Create a dependency tree such that project1(A) -> project2(A) -> project4(A)
            //                                                -> project3(A) -> project5(A)
            var commands = @"
[
    {command: 'A', dependsOn: [{kind: 'package', command: 'A'}]},
]";

            var config = Build(executeCommands: commands)
                .AddJavaScriptProject("@ms/project-1", "src/1", scriptCommands: new[] { ("A", "script A1") }, dependencies: new[] { "@ms/project-2", "@ms/project-3" })
                .AddJavaScriptProject("@ms/project-2", "src/2", scriptCommands: new[] { ("A", "script A2") }, dependencies: new[] { "@ms/project-4" })
                .AddJavaScriptProject("@ms/project-3", "src/3", scriptCommands: new (string, string)[] {},    dependencies: new[] { "@ms/project-5" })
                .AddJavaScriptProject("@ms/project-4", "src/4", scriptCommands: new[] { ("A", "script A2") })
                .AddJavaScriptProject("@ms/project-5", "src/5", scriptCommands: new[] { ("A", "script A2") })
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/1", "@ms/project-1"),
                ("src/2", "@ms/project-2"),
                ("src/3", "@ms/project-3"),
                ("src/4", "@ms/project-4"),
                ("src/5", "@ms/project-5"),
            });

            Assert.True(result.IsSuccess);

            var p1 = result.EngineState.RetrieveProcess("@ms/project-1", "A");
            var p2 = result.EngineState.RetrieveProcess("@ms/project-2", "A");
            var p3 = result.EngineState.RetrieveProcess("@ms/project-3", "A");
            var p4 = result.EngineState.RetrieveProcess("@ms/project-4", "A");
            var p5 = result.EngineState.RetrieveProcess("@ms/project-5", "A");

            Assert.True(p1 != null);
            Assert.True(p2 != null);
            Assert.True(p3 == null);
            Assert.True(p4 != null);
            Assert.True(p5 != null);


            // Now check dependencies. Since A on project3 is missing, we should get
            // project1(A)-> project2(A)->project4(A)
            //            -> project5(A)
            Assert.True(IsDependencyAndDependent(p4, p2));
            Assert.True(IsDependencyAndDependent(p2, p1));
            Assert.True(IsDependencyAndDependent(p5, p1));
        }

        private BuildXLEngineResult BuildDummyWithCommands(string commands)
        {
            var config = Build(executeCommands: commands)
                    .AddJavaScriptProject("@ms/project-A", "src/A")
                    .PersistSpecsAndGetConfiguration();

            return RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });
        }
    }
}
