// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.TestUtilities.XUnit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Yarn.IntegrationTests
{
    public class CustomJavaScriptIntegrationTests : CustomJavaScriptIntegrationTestBase
    {
        public CustomJavaScriptIntegrationTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // We don't actually need to execute anything, scheduling is enough
        protected override EnginePhases Phase => EnginePhases.Schedule;

        [Fact]
        public void CustomFileGraphAndScriptsInteraction()
        {
            // The graph is A -> B -> C and A -> C
            string customGraph = @"{
                '@ms/project-A' : {location: 'src/project-A', workspaceDependencies: []},
                '@ms/project-B' : {location: 'src/project-B', workspaceDependencies: ['@ms/project-A']},
                '@ms/project-C' : {location: 'src/project-C', workspaceDependencies: ['@ms/project-A', '@ms/project-B']}
            }";

            // For package A we return a map, for B a file pointing to a JSON and for C undefined, so C's original scripts should be picked up
            // in the end, each package should have its package name as part of its 'build' script command
            string customScripts = @"(packageName, location) => {
                                        switch (packageName) {
                                            case '@ms/project-A': {
                                                return Map.empty<string, FileContent>().add('build', 'do project-A build');
                                            }
                                            case '@ms/project-B': {
                                                return f`project-B-scripts.json`;
                                            }
                                            case '@ms/project-C': {
                                                return undefined;
                                            }
                                        }
                                    }";

            // Pass the custom graph and the custom scripts
            var config = Build(
                    customGraph: "f`customGraph.json`",
                    customScripts: customScripts)
                .AddJavaScriptProject("@ms/project-A", "src/project-A", scriptCommands: new[] { ("build", "do build")})
                .AddJavaScriptProject("@ms/project-B", "src/project-B", scriptCommands: new[] { ("build", "do build") })
                .AddJavaScriptProject("@ms/project-C", "src/project-C", scriptCommands: new[] { ("build", "do project-C build") })
                .PersistSpecsAndGetConfiguration();

            // Write a JSON file following package.json structure (but only 'scripts' section actually matter) for project B
            var customScriptsFile = config.Layout.SourceDirectory.Combine(PathTable, "project-B-scripts.json").ToString(PathTable);
            File.WriteAllText(customScriptsFile, @"{""scripts"": {""build"": ""do project-B build""}}");

            // Write a JSON file following Yarn workspaces structure for the custom graph
            var customGraphFile = config.Layout.SourceDirectory.Combine(PathTable, "customGraph.json").ToString(PathTable);
            File.WriteAllText(customGraphFile, customGraph);

            var result = RunCustomJavaScriptProjects(config);
            Assert.True(result.IsSuccess);

            var processA = result.EngineState.RetrieveProcess("@ms/project-A");
            var processB = result.EngineState.RetrieveProcess("@ms/project-B");
            var processC = result.EngineState.RetrieveProcess("@ms/project-C");

            // Let's assert the graph shape
            Assert.True(IsDependencyAndDependent(processA, processB));
            Assert.True(IsDependencyAndDependent(processB, processC));
            Assert.True(IsDependencyAndDependent(processA, processC));

            //Let's assert the custom scripts
            Assert.Contains("project-A", processA.Arguments.ToString(PathTable));
            Assert.Contains("project-B", processB.Arguments.ToString(PathTable));
            Assert.Contains("project-C", processC.Arguments.ToString(PathTable));
        }

        [Fact]
        public void CustomLiteralGraphCanBeProvided()
        {
            string customGraph = @"Map.empty<string, {location: RelativePath, workspaceDependencies: string[]}>()
                                    .add('@ms/project-A', {location: r`src/project-A`, workspaceDependencies: []})
                                    .add('@ms/project-B', {location: r`src/project-B`, workspaceDependencies: ['@ms/project-A']})";

            var config = Build(
                    customGraph: customGraph,
                    customScripts: "(packageName, location) => Map.empty<string, FileContent>().add('build', 'do custom build')")
                .AddJavaScriptProject("@ms/project-A", "src/project-A", scriptCommands: new[] { ("build", "do build") })
                .AddJavaScriptProject("@ms/project-B", "src/project-B", scriptCommands: new[] { ("build", "do build") })
                .PersistSpecsAndGetConfiguration();

            var result = RunCustomJavaScriptProjects(config);
            Assert.True(result.IsSuccess);

            var processA = result.EngineState.RetrieveProcess("@ms/project-A");
            var processB = result.EngineState.RetrieveProcess("@ms/project-B");

            Assert.True(IsDependencyAndDependent(processA, processB));
        }

        [Theory]
        [InlineData("undefined")]
        [InlineData("Map.empty<string, {location: RelativePath, workspaceDependencies: string[]}>().add('', undefined)")]
        [InlineData("Map.empty<string, {location: RelativePath, workspaceDependencies: string[]}>().add('project', undefined)")]
        [InlineData("Map.empty<string, {location: RelativePath, workspaceDependencies: string[]}>().add('project', {location: undefined, workspaceDependencies: undefined})")]
        [InlineData("Map.empty<string, {location: RelativePath, workspaceDependencies: string[]}>().add('project', {location: r`.`, workspaceDependencies: undefined})")]
        public void CustomGraphLiteralErrorHandling(string customGraph)
        {
            var config = Build(
                    customGraph: customGraph,
                    customScripts: "(packageName, location) => Map.empty<string, FileContent>().add('build', 'do custom build')")
                .AddJavaScriptProject("@ms/project-A", "src/project-A", scriptCommands: new[] { ("build", "do build") })
                .PersistSpecsAndGetConfiguration();

            var result = RunCustomJavaScriptProjects(config);
            Assert.False(result.IsSuccess);
            
            AssertErrorEventLogged(global::BuildXL.FrontEnd.Yarn.Tracing.LogEventId.ErrorReadingCustomProjectGraph);
            AssertErrorEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CannotBuildWorkspace);
        }

        [Theory]
        [InlineData(null, true)]
        [InlineData("this is not JSON", true)]
        [InlineData("{'' : {location: 'a/path', workspaceDependencies: []}}", true)]
        [InlineDataIfSupported(requiresWindowsBasedOperatingSystem: true, data: new object[] { "{'project1' : {location: 'invalid:path', workspaceDependencies: []}}", true})]
        [InlineData("{'project2' : {location: 'a/path', workspaceDependencies: null}}", true)]
        [InlineData("{'project3' : {location: 'a/path', workspaceDependencies: ['non-existent-proj']}}", false)]
        public void CustomGraphFileErrorHandling(string fileContent, bool producesCustomProjectGraphError)
        {
            var config = Build(
                    customGraph: "f`customGraph.json`",
                    customScripts: "(packageName, location) => Map.empty<string, FileContent>().add('build', 'do custom build')")
                .AddJavaScriptProject("@ms/project-A", "src/project-A", scriptCommands: new[] { ("build", "do build") })
                .PersistSpecsAndGetConfiguration();

            // Write a JSON file following Yarn workspaces structure for the custom graph
            if (fileContent != null)
            {
                var customGraphFile = config.Layout.SourceDirectory.Combine(PathTable, "customGraph.json").ToString(PathTable);
                File.WriteAllText(customGraphFile, fileContent);
            }

            var result = RunCustomJavaScriptProjects(config);
            Assert.False(result.IsSuccess);

            if (producesCustomProjectGraphError)
            {
                AssertErrorEventLogged(global::BuildXL.FrontEnd.Yarn.Tracing.LogEventId.ErrorReadingCustomProjectGraph);
            }
            AssertErrorEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CannotBuildWorkspace);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("this is not JSON")]
        [InlineData("{}")]
        [InlineData("{'scripts': {'' : 'script'}}")]
        public void FileCustomScriptsErrorHandling(string customScriptsContent)
        {
            string customGraph = @"Map.empty<string, {location: RelativePath, workspaceDependencies: string[]}>()
                                    .add('@ms/project-A', {location: r`src/custom/project-A`, workspaceDependencies: []})";

            var config = Build(
                    customGraph: customGraph,
                    customScripts: "(packageName, location) => f`customScripts.json`")
                .AddJavaScriptProject("@ms/project-A", "src/project-A", scriptCommands: new[] { ("build", "do build") })
                .PersistSpecsAndGetConfiguration();

            // Write a JSON file following Yarn workspaces structure for the custom graph
            if (customScriptsContent != null)
            {
                var customGraphFile = config.Layout.SourceDirectory.Combine(PathTable, "customScripts.json").ToString(PathTable);
                File.WriteAllText(customGraphFile, customScriptsContent);
            }

            var result = RunCustomJavaScriptProjects(config);
            Assert.False(result.IsSuccess);

            AssertErrorEventLogged(global::BuildXL.FrontEnd.JavaScript.Tracing.LogEventId.CannotLoadScriptsFromJsonFile);
            AssertErrorEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CannotBuildWorkspace);
        }

        [Theory]
        [InlineData("Map.empty<string, FileContent>().add('', 'do custom build')")]
        [InlineData("Map.empty<string, FileContent>().add('build', undefined)")]
        public void LiteralCustomScriptsErrorHandling(string customScriptsContent)
        {
            string customGraph = @"Map.empty<string, {location: RelativePath, workspaceDependencies: string[]}>()
                                    .add('@ms/project-A', {location: r`src/custom/project-A`, workspaceDependencies: []})";

            var config = Build(
                    customGraph: customGraph,
                    customScripts: $"(packageName, location) => {customScriptsContent}")
                .AddJavaScriptProject("@ms/project-A", "src/project-A", scriptCommands: new[] { ("build", "do build") })
                .PersistSpecsAndGetConfiguration();

            var result = RunCustomJavaScriptProjects(config);
            Assert.False(result.IsSuccess);

            AssertErrorEventLogged(global::BuildXL.FrontEnd.JavaScript.Tracing.LogEventId.CustomScriptsFailure);
            AssertErrorEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CannotBuildWorkspace);
        }

        [Theory]
        [InlineData("LogsDirectory", true)]
        [InlineDataIfSupported(requiresWindowsBasedOperatingSystem: true, data: new object[] {"ProgramFiles", true})]
        [InlineDataIfSupported(requiresWindowsBasedOperatingSystem: true, data: new object[] {"UserDefinedMount", false})]
        public void MountsAreAccesibleDuringCustomScriptEvaluation(string mountName, bool expectSuccess)
        {
            string customGraph = @"Map.empty<string, {location: RelativePath, workspaceDependencies: string[]}>()
                                    .add('@ms/project-A', {location: r`src/custom/project-A`, workspaceDependencies: []})";

            // Retrieve the mount name during custom script evaluation
            string spec = @$"
export function customScripts(packageName: string, location: RelativePath): File | Map<string, FileContent> {{
    const t = Context.getMount('{mountName}');
    return Map.empty<string, FileContent>().add('build', 'do build');
}}";
            
            // Define a user-defined mount, which shouldn't be visible
            var config = Build(
                    customGraph: customGraph,
                    customScripts: $"importFile(f`customScripts.dsc`).customScripts",
                    configExtension: "mounts: [{name: a`UserDefinedMount`, path: p`Out/Bin`, trackSourceFileChanges: true, isWritable: true, isReadable: true}]")
                  .AddJavaScriptProject("@ms/project-A", "src/project-A", scriptCommands: new[] { ("build", "do build") })
                .AddSpec("customScripts.dsc", spec)
                .PersistSpecsAndGetConfiguration();

            var result = RunCustomJavaScriptProjects(config);

            if (expectSuccess)
            {
                Assert.True(result.IsSuccess);
            }
            else
            {
                Assert.False(result.IsSuccess);
                AssertErrorEventLogged(global::BuildXL.FrontEnd.Script.Tracing.LogEventId.GetMountNameNotFound);

                // When GetMount fails, the error message uses GetMountNames to list the available mounts. Partially test that here by validating the log message.
                string mountNotFoundMessage = EventListener.GetLogMessagesForEventId((int)global::BuildXL.FrontEnd.Script.Tracing.LogEventId.GetMountNameNotFound).Single();
                XAssert.Contains(mountNotFoundMessage, "LogsDirectory", "ProgramFiles");

                AssertErrorEventLogged(global::BuildXL.FrontEnd.JavaScript.Tracing.LogEventId.CustomScriptsFailure);
                AssertErrorEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CannotBuildWorkspace);
            }
        }
    }
}
