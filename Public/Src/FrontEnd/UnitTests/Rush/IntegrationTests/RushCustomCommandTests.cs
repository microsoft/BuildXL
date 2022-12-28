// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;
using LogEventId = global::BuildXL.FrontEnd.JavaScript.Tracing.LogEventId;

namespace Test.BuildXL.FrontEnd.Rush
{
    [Trait("Category", "RushCustomCommandsTests")]
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
                .AddJavaScriptProject("@ms/project-A", "src/A")
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
            // Schedule a rush project with a build command 'npm run execute', and extend it to be
            // 'npm run execute --test' via a custom command. Purposely use a command with spaces so
            // we can verify correct escaping on unix
            var config = Build(customRushCommands: "[{command: 'build', extraArguments: '--test --me'}]")
               .AddJavaScriptProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "npm run execute") })
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.True(result.IsSuccess);

            var pip = result.EngineState.RetrieveProcess("@ms/project-A", "build");
            var arguments = pip.Arguments.ToString(PathTable);

            string testCommand = "npm run execute --test --me";
            
            // On Linux/Mac we want to make sure the whole command is enclosed in double quotes
            if (!OperatingSystemHelper.IsWindowsOS)
            {
                testCommand = $"\" {testCommand} \"";
            }

            Assert.Contains(testCommand, arguments);
        }

        [Fact]
        public void CustomRushCommandOnlyAffectsSpecifiedScript()
        {
            // Two projects, one with 'build', the other one with 'test'.
            // Extend 'build'to be 'execute --test' via a custom command
            var config = Build(customRushCommands: "[{command: 'build', extraArguments: '--test'}]", executeCommands: "['build', 'test']")
               .AddJavaScriptProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "execute") })
               .AddJavaScriptProject("@ms/project-B", "src/B", scriptCommands: new[] { ("test", "execute") })
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
        public void ComplexCustomCommand()
        {
            var path = X("/c/absolute/path");
            // Exercise custom commands with other types
            var config = Build(customRushCommands: $"[{{command: 'build', extraArguments: ['--test', a`atom`, r`relative/path`, p`{path}`]}}]")
               .AddJavaScriptProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "execute") })
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.True(result.IsSuccess);

            var pip = result.EngineState.RetrieveProcess("@ms/project-A", "build");
            Assert.Contains(
                $"execute --test atom {RelativePath.Create(StringTable, "relative/path").ToString(StringTable)} {AbsolutePath.Create(PathTable, path).ToString(PathTable)}", 
                pip.Arguments.ToString(PathTable));
        }
    }
}
