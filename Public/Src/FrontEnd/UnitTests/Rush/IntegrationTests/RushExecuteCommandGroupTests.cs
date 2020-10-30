// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;
using LogEventId = global::BuildXL.FrontEnd.JavaScript.Tracing.LogEventId;

namespace Test.BuildXL.FrontEnd.Rush
{
    [Trait("Category", "RushCustomCommandsTests")]
    public class RushExecuteCommandGroupTests : RushIntegrationTestBase
    {
        public RushExecuteCommandGroupTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // We don't actually need to execute anything, scheduling is enough
        protected override EnginePhases Phase => EnginePhases.Schedule;

        [Theory]
        [InlineData("[{commandName: '', commands: ['build', 'test'], dependsOn:[]}]", LogEventId.JavaScriptCommandIsEmpty)]
        [InlineData("[{commandName: undefined, commands: ['build', 'test'], dependsOn:[]}]", LogEventId.JavaScriptCommandIsEmpty)]
        [InlineData("[{commandName: undefined, commands: ['', 'test'], dependsOn:[]}]", LogEventId.JavaScriptCommandIsEmpty)]
        [InlineData("[{commandName: undefined, commands: [undefined, 'test'], dependsOn:[]}]", LogEventId.JavaScriptCommandIsEmpty)]
        [InlineData("[{commandName: 'test', commands: ['test'], dependsOn:[]}]", LogEventId.JavaScriptCommandGroupCanOnlyContainRegularCommands)]
        [InlineData("[{commandName: 'build-and-test', commands: ['build', 'test'], dependsOn:[]}, {commandName: 'prebuild', commands: ['build-and-test'], dependsOn:[]}]", LogEventId.JavaScriptCommandGroupCanOnlyContainRegularCommands)]
        public void InvalidGroupCommands(string executeCommand, LogEventId expectedErrorCode)
        {
            var config = Build(executeCommands: executeCommand)
                .AddJavaScriptProject("@ms/project-A", "src/A")
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.False(result.IsSuccess);

            AssertErrorEventLogged(expectedErrorCode);
        }

        [Fact]
        public void SimpleGroupCommand()
        {
            var config = Build(executeCommands: "[{commandName: 'build-and-execute', commands:['build', 'execute'], dependsOn:[]}]")
               .AddJavaScriptProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "build script"), ("execute", "execute script") })
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            // There should be a single process containing both build and execute script commands
            Assert.True(result.IsSuccess);
            var process = result.EngineState.RetrieveProcesses().Single();
            Assert.Contains("(build script)&&(execute script)", process.Arguments.ToString(PathTable));
        }

        [Fact]
        public void MissingCommandInGroupIsSkipped()
        {
            // The groups command contains build and execute, but the project only contains build
            var config = Build(executeCommands: "[{commandName: 'build-and-execute', commands:['build', 'execute'], dependsOn:[]}]")
               .AddJavaScriptProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "build script")})
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            // There should be a single process containing build command
            Assert.True(result.IsSuccess);
            var process = result.EngineState.RetrieveProcesses().Single();
            Assert.Contains("(build script)", process.Arguments.ToString(PathTable));
        }

        [Theory]
        [InlineData("'test'")]
        [InlineData("{command: 'test', dependsOn:[{command: 'build-and-execute', kind: 'local'}]}")]
        public void DependencyToGroupCommand(string testCommand)
        {
            var config = Build(executeCommands: $"[{{commandName: 'build-and-execute', commands:['build', 'execute'], dependsOn:[]}}, {testCommand}]")
               .AddJavaScriptProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "build script"), ("execute", "execute script"), ("test", "test script") })
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.True(result.IsSuccess);
            var buildAndExecute = result.EngineState.RetrieveProcess("@ms/project-A", "build-and-execute");
            var test = result.EngineState.RetrieveProcess("@ms/project-A", "test");

            // Test should depend on build-and-execute (when there is a direct dependency on the group, or a dependency on one of its members)
            Assert.True(IsDependencyAndDependent(buildAndExecute, test));
        }

        [Theory]
        [InlineData("'postbuild'")]
        [InlineData("{command: 'postbuild', dependsOn: [{command: 'build', kind: 'local'}]}")]
        public void AbsentGroupCommand(string postBuildCommand)
        {
            // Specify commands such that: prebuild <- build <-postbuild, but making 'build' a group command. For two version of this test: the first one
            // postbuild points to the group command, in the second one to one of its members.
            // Provide a project which only has prebuild and postbuild
            var config = Build(executeCommands: $"['prebuild', {{commandName: 'build-as-group', commands:['build'], dependsOn:[{{command: 'prebuild', kind: 'local'}}]}}, {postBuildCommand}]")
               .AddJavaScriptProject("@ms/project-A", "src/A", scriptCommands: new[] { ("prebuild", "prebuild script"), ("postbuild", "postbuild script") })
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.True(result.IsSuccess);
            var prebuild = result.EngineState.RetrieveProcess("@ms/project-A", "prebuild");
            var buildAsGroup = result.EngineState.RetrieveProcess("@ms/project-A", "build-as-group");
            var postbuild = result.EngineState.RetrieveProcess("@ms/project-A", "postbuild");

            // The group should not be there, since none of its command members are there
            Assert.Null(buildAsGroup);

            // Still postbuild should depend on prebuild
            Assert.True(IsDependencyAndDependent(prebuild, postbuild));
        }

        [Theory]
        [InlineData("'build'")]
        [InlineData("{command: 'build', dependsOn: [{command: 'prebuild', kind: 'local'}]}")]
        public void AbsentCommandToGroupCommand(string buildCommand)
        {
            // Specify commands such that: prebuild <- build <-postbuild, but making 'prebuild' a group command. For two version of this test: the first one
            // build points to the group command, in the second one to one of its members.
            // Provide a project which only has prebuild and postbuild
            var config = Build(executeCommands: $"[{{commandName: 'prebuild-as-group', commands:['prebuild'], dependsOn:[]}}, {buildCommand}, 'postbuild']")
               .AddJavaScriptProject("@ms/project-A", "src/A", scriptCommands: new[] { ("prebuild", "prebuild script"), ("postbuild", "postbuild script") })
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.True(result.IsSuccess);
            var prebuildAsGroup = result.EngineState.RetrieveProcess("@ms/project-A", "prebuild-as-group");
            var build = result.EngineState.RetrieveProcess("@ms/project-A", "build");
            var postbuild = result.EngineState.RetrieveProcess("@ms/project-A", "postbuild");

            // The build command should not be there
            Assert.Null(build);

            // Still postbuild should depend on prebuild
            Assert.True(IsDependencyAndDependent(prebuildAsGroup, postbuild));
        }
    }
}
