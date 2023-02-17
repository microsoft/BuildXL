// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.TestUtilities.XUnit;
using Xunit;
using Xunit.Abstractions;
using LogEventId = global::BuildXL.FrontEnd.JavaScript.Tracing.LogEventId;

namespace Test.BuildXL.FrontEnd.Rush
{
    [Trait("Category", "BxlRushConfigurationTests")]
    public class BxlRushConfigurationTests : RushIntegrationTestBase
    {
        public BxlRushConfigurationTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // We don't actually need to execute anything, scheduling is enough
        protected override EnginePhases Phase => EnginePhases.Schedule;

        [Fact]
        public void SimpleRushConfigurationFileIsHonored()
        {
            var config = Build()
                .AddJavaScriptProject("@ms/project-A", "src/A")
                .AddBxlConfigurationFile("src/A", @"
{
    ""outputDirectories"": [""../output/dir""],
    ""sourceFiles"": [""input/file""]
}")
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.True(result.IsSuccess);

            var pip = result.EngineState.RetrieveProcess("@ms/project-A");
            Assert.True(pip.DirectoryOutputs.Any(d => d.Path == GetPathRelativeToSourceRoot(config, "src/output/dir")));
            Assert.True(pip.Dependencies.Any(f => f.Path == GetPathRelativeToSourceRoot(config, "src/A/input/file")));
        }

        [Theory]
        [InlineData(@"[""../output/dir""]", new[] { "src/output/dir"})]
        [InlineData(@"[""../output/dir"", ""../another/dir""]", new[] { "src/output/dir", "src/another/dir" })]
        [InlineDataIfSupported(requiresWindowsBasedOperatingSystem: true, data: new object[] {@"[""<workspaceDir>/output"", ""C:\\foo""]", new[] { "output", "C:\\foo" }})]
        [InlineDataIfSupported(requiresUnixBasedOperatingSystem: true, data: new object[] {@"[""<workspaceDir>/output"", ""/foo""]", new[] { "output", "/foo" }})]
        public void PathPatternsAreHonored(string outputDirectoriesJSON, string[] expectedOutputDirectories)
        {
            var config = Build()
                .AddJavaScriptProject("@ms/project-A", "src/A")
                .AddBxlConfigurationFile("src/A", @$"
{{
    ""outputDirectories"": {outputDirectoriesJSON}
}}")
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.True(result.IsSuccess);

            var pip = result.EngineState.RetrieveProcess("@ms/project-A");
            
            foreach (var expectedOutputDir in expectedOutputDirectories)
            {
                Assert.True(pip.DirectoryOutputs.Any(d => d.Path == GetPathRelativeToSourceRoot(config, expectedOutputDir)));
            }
        }

        [Fact]
        public void PathsCanBeScriptSpecific()
        {
            // Create a project and schedule its 'build' and 'test' script. Only define output directories for 'build'
            var config = Build(executeCommands: "['build', 'test']")
                .AddJavaScriptProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "build A"), ("test", "test A")})
                .AddBxlConfigurationFile("src/A", @"
{
    ""outputDirectories"": [{""path"": ""../output/dir"", ""targetScripts"": [""build""]}]
}")
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.True(result.IsSuccess);

            var build = result.EngineState.RetrieveProcess("@ms/project-A", "build");
            var test = result.EngineState.RetrieveProcess("@ms/project-A", "test");

            // 'build' pip should have the extra dir, 'test' pip should not
            Assert.True(build.DirectoryOutputs.Any(d => d.Path == GetPathRelativeToSourceRoot(config, "src/output/dir")));
            Assert.False(test.Dependencies.Any(f => f.Path == GetPathRelativeToSourceRoot(config, "src/output/dir")));
        }

        [Theory]
        [InlineData(@"undefined")]
        [InlineDataIfSupported(requiresWindowsBasedOperatingSystem: true, data: new object[] {@"""invalid|path"""})]
        [InlineData(@"invalid {{ json")]
        public void MalformedConfigurationFileIsHandled(string outputDirectories)
        {
            var config = Build()
                .AddJavaScriptProject("@ms/project-A", "src/A")
                .AddBxlConfigurationFile("src/A", @$"
{{
    ""outputDirectories"": [{outputDirectories}]
}}")
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.False(result.IsSuccess);

            AssertErrorEventLogged(LogEventId.ProjectGraphConstructionError);
            AssertErrorEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CannotBuildWorkspace);
        }

        private AbsolutePath GetPathRelativeToSourceRoot(ICommandLineConfiguration config, string absoluteOrRelativePath)
        {
            if (AbsolutePath.TryCreate(PathTable, absoluteOrRelativePath, out var absolutePath))
            {
                return absolutePath;
            }

            return config.Layout.SourceDirectory.Combine(PathTable, RelativePath.Create(StringTable, absoluteOrRelativePath));
        }
    }
}
