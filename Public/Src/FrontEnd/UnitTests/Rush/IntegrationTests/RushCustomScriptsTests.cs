// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Rush
{
    [Trait("Category", "RushCustomScriptsTests")]
    public class RushCustomScriptsTests : RushIntegrationTestBase
    {
        public RushCustomScriptsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // We don't actually need to execute anything, scheduling is enough
        protected override EnginePhases Phase => EnginePhases.Schedule;

        [Fact]
        public void CustomScriptsOverridesDefaultScripts()
        {
            // Create a project but provide a custom script callback
            var config = Build(customScripts: "(packageName, location) => Map.empty<string, FileContent>().add('build', 'do custom build')")
                .AddJavaScriptProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "do build") })
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
            });

            Assert.True(result.IsSuccess);

            var process = result.EngineState.RetrieveProcess("@ms/project-A");
            Assert.NotNull(process);

            // The arguments should match the ones specified by the callback
            Assert.Contains("custom", process.Arguments.ToString(PathTable));
        }

        [Fact]
        public void CallbackArgumentsAreHonored()
        {
            // check for expected package name and location to return a package-specific script
            string customScripts = @"(packageName, location) => 
                                        (packageName === '@ms/project-A' && location === r`src/A`) 
                                        ? Map.empty<string, FileContent>().add('build', 'do custom project-A build')
                                        : Map.empty<string, FileContent>().add('build', 'do custom project-B build')";

            var config = Build(customScripts: customScripts)
                .AddJavaScriptProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "do build") })
                .AddJavaScriptProject("@ms/project-B", "src/B", scriptCommands: new[] { ("build", "do build") })
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
                ("src/B", "@ms/project-B"),
            });

            Assert.True(result.IsSuccess);

            var processA = result.EngineState.RetrieveProcess("@ms/project-A");
            Assert.NotNull(processA);

            var processB = result.EngineState.RetrieveProcess("@ms/project-B");
            Assert.NotNull(processB);

            // The arguments should match the ones specified by the callback
            Assert.Contains("project-A", processA.Arguments.ToString(PathTable));
            Assert.Contains("project-B", processB.Arguments.ToString(PathTable));
        }

        [Fact]
        public void FileWithScriptsCanBeReturned()
        {
            // Custom script callback returns a file to read the scripts section from
            var config = Build(customScripts: "(packageName, location) => f`customScripts.json`")
                .AddJavaScriptProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "do build") })
                .PersistSpecsAndGetConfiguration();

            // Write a JSON file following package.json structure (but only 'scripts' section actually matter)
            var customScriptsFile = config.Layout.SourceDirectory.Combine(PathTable, "customScripts.json").ToString(PathTable);
            File.WriteAllText(customScriptsFile, @"{""scripts"": {""build"": ""do custom build""}}");

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
            });

            Assert.True(result.IsSuccess);

            var process = result.EngineState.RetrieveProcess("@ms/project-A");
            Assert.NotNull(process);

            // The arguments should match the ones specified by the callback
            Assert.Contains("custom", process.Arguments.ToString(PathTable));
        }

        [Fact]
        public void UndefinedScriptsImpliesRegularScripts()
        {
            // Create a project but provide a custom script callback
            var config = Build(customScripts: "(packageName, location) => undefined")
                .AddJavaScriptProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "do build") })
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
            });

            Assert.True(result.IsSuccess);

            var process = result.EngineState.RetrieveProcess("@ms/project-A");
            Assert.NotNull(process);

            // The arguments should match the ones specified by the package
            Assert.Contains("do build", process.Arguments.ToString(PathTable));
        }
    }
}
