// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Lage
{
    /// <summary>
    /// Tests group commands for Lage
    /// </summary>
    /// <remarks>
    /// This deserves a test suite on its own since the equivalent RushExecuteCommandGroupTests uses the Bxl provided execution semantics.
    /// </remarks>
    public class LageCommandGroupTest : LageIntegrationTestBase
    {
        public LageCommandGroupTest(ITestOutputHelper output)
            : base(output)
        {
        }

        /// <summary>
        /// Run up to schedule
        /// </summary>
        protected override EnginePhases Phase => EnginePhases.Schedule;

        [Fact]
        public void SimpleGroupCommand()
        {
            var config = Build(executeCommands: "[{commandName: 'build-and-test', commands:['build', 'test']}]")
               .AddJavaScriptProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "build script"), ("test", "test script") })
               .PersistSpecsAndGetConfiguration();

            var result = RunLageProjects(config);

            // There should be a single process containing both build and test script commands
            Assert.True(result.IsSuccess);
            var process = result.EngineState.RetrieveProcesses().Single();
            Assert.Contains("(npm run build)&&(npm run test)", process.Arguments.ToString(PathTable));
        }

        [Fact]
        public void GroupDependenciesAreMerged()
        {
            var config = Build(executeCommands: "[{commandName: 'prepare-and-posttest', commands:['prepare', 'posttest']}]")
                .AddJavaScriptProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "build script") })
                .AddJavaScriptProject("@ms/project-B", "src/B", scriptCommands: new[] { ("test", "test script") })
                .AddJavaScriptProject("@ms/project-C", "src/C", scriptCommands: new[] { ("prepare", "prepare script"), ("posttest", "posttest script") }, dependencies: new[] { "@ms/project-A", "@ms/project-B" })
               .PersistSpecsAndGetConfiguration();

            var result = RunLageProjects(config);

            Assert.True(result.IsSuccess);

            // We should have 3 processes (A, build), (B, test), (C, prepare-and-posttest)
            Assert.Equal(3, result.EngineState.RetrieveProcesses().Count());

            var build = result.EngineState.RetrieveProcess("@ms/project-A", "build#build");
            var test = result.EngineState.RetrieveProcess("@ms/project-B", "test#test");
            var prepareAndPostTest = result.EngineState.RetrieveProcess("@ms/project-C", "prepare-and-posttest#prepare-and-posttest");

            // PrepareAndPostTest should depend on both build and test
            Assert.True(IsDependencyAndDependent(build, prepareAndPostTest));
            Assert.True(IsDependencyAndDependent(test, prepareAndPostTest));
        }

        [Fact]
        public void MissingCommandInGroupIsSkipped()
        {
            // The groups command contains build and test, but the project only contains build
            var config = Build(executeCommands: "[{commandName: 'build-and-test', commands:['build', 'test']}]")
               .AddJavaScriptProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "build script") })
               .PersistSpecsAndGetConfiguration();

            var result = RunLageProjects(config);

            // There should be a single process containing build command
            Assert.True(result.IsSuccess);
            var process = result.EngineState.RetrieveProcesses().Single();
            Assert.Contains("(npm run build)", process.Arguments.ToString(PathTable));
            Assert.DoesNotContain("(npm run test)", process.Arguments.ToString(PathTable));
        }

        [Fact]
        public void DependencyToGroupCommand()
        {
            var config = Build(executeCommands: $"[{{commandName: 'build-and-test', commands:['build', 'test']}}, 'lint']")
               .AddJavaScriptProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "build script"), ("test", "test script"), ("lint", "lint script") })
               .AddJavaScriptProject("@ms/project-B", "src/B", scriptCommands: new[] { ("build", "build script") }, dependencies: new[] { "@ms/project-A" })
               .PersistSpecsAndGetConfiguration();

            var result = RunLageProjects(config);

            Assert.True(result.IsSuccess);
            var buildAndTestA = result.EngineState.RetrieveProcess("@ms/project-A", "build-and-test#build-and-test");
            var lintA = result.EngineState.RetrieveProcess("@ms/project-A", "lint#lint");
            var buildAndTestB = result.EngineState.RetrieveProcess("@ms/project-B", "build-and-test#build-and-test");

            // We should have 3 processes (A, build-and-test), (A, lint), (B, build-and-test)
            Assert.Equal(3, result.EngineState.RetrieveProcesses().Count());

            // Lint should depend on build-and-test for A
            Assert.True(IsDependencyAndDependent(buildAndTestA, lintA));
            // build-and-test for B should depend on build-and-test for A
            Assert.True(IsDependencyAndDependent(buildAndTestA, buildAndTestB));
        }
    }
}
