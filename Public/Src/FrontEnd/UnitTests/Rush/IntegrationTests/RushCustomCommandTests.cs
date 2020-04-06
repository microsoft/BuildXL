// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.FrontEnd.Rush.IntegrationTests;
using Xunit;
using Xunit.Abstractions;
using LogEventId = global::BuildXL.FrontEnd.Rush.Tracing.LogEventId;

namespace Test.BuildXL.FrontEnd.Rush
{
    public class RushCustomCommandsTests : RushIntegrationTestBase
    {
        public RushCustomCommandsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // We don't actually need to execute anything, scheduling is enough
        protected override EnginePhases Phase => EnginePhases.Schedule;

        [Theory]
        [InlineData("[{command: '', extraArguments: 'extra'}]")]
        [InlineData("[{command: undefined, extraArguments: 'extra'}]")]
        [InlineData("[{command: '', extraArguments: undefined}]")]
        [InlineData("[{command: '', extraArguments: ''}]")]
        [InlineData("[{command: '', extraArguments: [undefined]}]")]
        public void InvalidCustomRushCommands(string customCommands)
        {
            var config = Build(customRushCommands: customCommands)
                .AddRushProject("@ms/project-A", "src/A")
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.False(result.IsSuccess);

            AssertErrorEventLogged(LogEventId.InvalidResolverSettings);
        }

        [Fact]
        public void SimpleCustomRushCommand()
        {
            // Schedule a rush project with a build command 'execute', and extend it to be
            // 'execute --test' via a custom command
            var config = Build(customRushCommands: "[{command: 'build', extraArguments: '--test'}]")
               .AddRushProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "execute") })
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.True(result.IsSuccess);

            var pip = result.EngineState.RetrieveProcess("@ms/project-A", "build");
            Assert.Contains("execute --test", pip.Arguments.ToString(PathTable));
        }

        [Fact]
        public void CustomRushCommandOnlyAffectsSpecifiedScript()
        {
            // Two projects, one with 'build', the other one with 'test'.
            // Extend 'build'to be 'execute --test' via a custom command
            var config = Build(customRushCommands: "[{command: 'build', extraArguments: '--test'}]", executeCommands: "['build', 'test']")
               .AddRushProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "execute") })
               .AddRushProject("@ms/project-B", "src/B", scriptCommands: new[] { ("test", "execute") })
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
                ("src/B", "@ms/project-B")
            });

            Assert.True(result.IsSuccess);

            // 'build' on pip A should be extended, 'test' on pip B should not
            var pipA = result.EngineState.RetrieveProcess("@ms/project-A", "build");
            Assert.Contains("execute --test", pipA.Arguments.ToString(PathTable));
            var pipB = result.EngineState.RetrieveProcess("@ms/project-B", "test");
            Assert.DoesNotContain("execute --test", pipB.Arguments.ToString(PathTable));
        }

        [Fact]
        public void ComplextCustomCommand()
        {
            // Exercise custom commands with other types
            var config = Build(customRushCommands: "[{command: 'build', extraArguments: ['--test', a`atom`, r`relative/path`, p`C:/absolute/path`]}]")
               .AddRushProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "execute") })
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.True(result.IsSuccess);

            var pip = result.EngineState.RetrieveProcess("@ms/project-A", "build");
            Assert.Contains(
                $"execute --test atom {RelativePath.Create(StringTable, "relative/path").ToString(StringTable)} {AbsolutePath.Create(PathTable, "C:/absolute/path").ToString(PathTable)}", 
                pip.Arguments.ToString(PathTable));
        }
    }
}
