// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;
using LogEventId = global::BuildXL.FrontEnd.JavaScript.Tracing.LogEventId;
using CoreLogEventId = global::BuildXL.FrontEnd.Core.Tracing;

namespace Test.BuildXL.FrontEnd.Rush
{
    [Trait("Category", "RushAdditionalDependenciesTests")]
    public class RushAdditionalDependenciesTests : RushIntegrationTestBase
    {
        public RushAdditionalDependenciesTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // We don't actually need to execute anything, scheduling is enough
        protected override EnginePhases Phase => EnginePhases.Schedule;

        [Fact]
        public void AddSimpleProjectDependency()
        {
            // Force a A <- B dependency
            var config = Build(additionalDependencies: "[{dependencies:['@ms/project-A'], dependents: ['@ms/project-B']}]")
                .AddJavaScriptProject("@ms/project-A", "src/A")
                .AddJavaScriptProject("@ms/project-B", "src/B")
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
                ("src/B", "@ms/project-B")
            });

            Assert.True(result.IsSuccess);
            var pipA = result.EngineState.RetrieveProcess("@ms/project-A");
            var pipB = result.EngineState.RetrieveProcess("@ms/project-B");

            Assert.True(IsDependencyAndDependent(pipA, pipB));
        }

        [Fact]
        public void AddMultipleProjectDependencies()
        {
            // Force A <- B, C and B <- C dependency
            var config = Build(additionalDependencies: "[{dependencies:['@ms/project-A'], dependents: ['@ms/project-B', '@ms/project-C']}, {dependencies:['@ms/project-B'], dependents: ['@ms/project-C']}]")
                .AddJavaScriptProject("@ms/project-A", "src/A")
                .AddJavaScriptProject("@ms/project-B", "src/B")
                .AddJavaScriptProject("@ms/project-C", "src/C")
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
                ("src/B", "@ms/project-B"),
                ("src/C", "@ms/project-C")
            });

            Assert.True(result.IsSuccess);
            var pipA = result.EngineState.RetrieveProcess("@ms/project-A");
            var pipB = result.EngineState.RetrieveProcess("@ms/project-B");
            var pipC = result.EngineState.RetrieveProcess("@ms/project-C");

            Assert.True(IsDependencyAndDependent(pipA, pipB));
            Assert.True(IsDependencyAndDependent(pipA, pipC));
            Assert.True(IsDependencyAndDependent(pipB, pipC));
        }

        [Fact]
        public void AdditionalDependencyOnTopOfRegularDependencies()
        {
            // Force A <- B and defined B <- C in package.json
            var config = Build(additionalDependencies: "[{dependencies:['@ms/project-A'], dependents: ['@ms/project-B']}]")
                .AddJavaScriptProject("@ms/project-A", "src/A")
                .AddJavaScriptProject("@ms/project-B", "src/B")
                .AddJavaScriptProject("@ms/project-C", "src/C", dependencies: new[] { "@ms/project-B" })
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
                ("src/B", "@ms/project-B"),
                ("src/C", "@ms/project-C")
            });

            Assert.True(result.IsSuccess);
            var pipA = result.EngineState.RetrieveProcess("@ms/project-A");
            var pipB = result.EngineState.RetrieveProcess("@ms/project-B");
            var pipC = result.EngineState.RetrieveProcess("@ms/project-C");

            Assert.True(IsDependencyAndDependent(pipA, pipB));
            Assert.True(IsDependencyAndDependent(pipB, pipC));
        }

        [Fact]
        public void CyclesWithAdditionalDependenciesAreCaught()
        {
            // Force B <- A and define A <- B in package.json
            var config = Build(additionalDependencies: "[{dependencies:['@ms/project-B'], dependents: ['@ms/project-A']}]")
                .AddJavaScriptProject("@ms/project-A", "src/A")
                .AddJavaScriptProject("@ms/project-B", "src/B", dependencies: new[] { "@ms/project-A"})
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
                ("src/B", "@ms/project-B"),
            });

            // We should get a cycle
            Assert.False(result.IsSuccess);
            AssertErrorEventLogged(LogEventId.ProjectGraphConstructionError);
        }

        [Theory]
        [InlineData("importFrom('myModule').myFile", true)]
        [InlineData("importFrom('myModule').myDirectory", false)]
        public void AddLazyArtifactDependency(string expression, bool isFile)
        {
            // Define a file dependency on project A coming from another module
            var config = Build(additionalDependencies: $"[{{dependencies:[{{expression: \"{expression}\"}}], dependents: ['@ms/project-A']}}]", addDScriptResolver: true)
                .AddJavaScriptProject("@ms/project-A", "src/A")
                .AddSpec("module.config.dsc", "module({name: 'myModule'});")
                .AddSpec(@"
@@public export const myFile = f`aFile`;
@@public export const myDirectory = Transformer.sealPartialDirectory(d`.`, []);")
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
            });

            Assert.True(result.IsSuccess);

            // Verify the dependency is there
            var pipA = result.EngineState.RetrieveProcess("@ms/project-A");
            if (isFile)
            {
                Assert.True(pipA.Dependencies.Any(file => file.Path.GetName(PathTable) == PathAtom.Create(StringTable, "aFile")));
            }
            else 
            {
                Assert.True(pipA.DirectoryDependencies.Any(directory => directory.Path == config.Layout.SourceDirectory));
            }
        }
        
        [Fact]
        public void AddMultipleLazyArtifactDependency()
        {
            // Define two file dependencies on project A and B coming from another module
            // Undefined dependencies should just be ignored
            var config = Build(additionalDependencies: @"
[
{dependencies:[{expression: ""undefined""}], dependents: ['@ms/project-A']},
{dependencies:[{expression: ""importFrom('myModule').myFile""}], dependents: ['@ms/project-A']},
{dependencies:[{expression: ""importFrom('myModule').myOtherFile""}], dependents: ['@ms/project-A']},
{dependencies:[{expression: ""importFrom('myModule').myFile""}, {expression: ""importFrom('myModule').myOtherFile""}], dependents: ['@ms/project-B']},
]", addDScriptResolver: true)
                .AddJavaScriptProject("@ms/project-A", "src/A")
                .AddJavaScriptProject("@ms/project-B", "src/B")
                .AddSpec("module.config.dsc", "module({name: 'myModule'});")
                .AddSpec(@"
@@public export const myFile = f`aFile`;
@@public export const myOtherFile = f`anotherFile`;")
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
                ("src/B", "@ms/project-B"),
            });

            Assert.True(result.IsSuccess);

            // Verify the dependencies are there
            var pipA = result.EngineState.RetrieveProcess("@ms/project-A");
            var pipB = result.EngineState.RetrieveProcess("@ms/project-B");

            Assert.True(pipA.Dependencies.Any(file => file.Path.GetName(PathTable) == PathAtom.Create(StringTable, "aFile")));
            Assert.True(pipB.Dependencies.Any(file => file.Path.GetName(PathTable) == PathAtom.Create(StringTable, "aFile")));
            Assert.True(pipA.Dependencies.Any(file => file.Path.GetName(PathTable) == PathAtom.Create(StringTable, "anotherFile")));
            Assert.True(pipB.Dependencies.Any(file => file.Path.GetName(PathTable) == PathAtom.Create(StringTable, "anotherFile")));
        }

        [Theory]
        [InlineData("not-DScript-code", new[] { CoreLogEventId.LogEventId.CheckerError, CoreLogEventId.LogEventId.CannotBuildWorkspace })]
        [InlineData("importFrom('non-existent-module').myValue", new[] { CoreLogEventId.LogEventId.CannotBuildWorkspace })]
        // This should fail wrt expected type (File | StaticDirectory)
        [InlineData("true", new[] { CoreLogEventId.LogEventId.CheckerError, CoreLogEventId.LogEventId.CannotBuildWorkspace })]
        [InlineData("[f`AFile.txt`][15]", new[] { global::BuildXL.FrontEnd.Script.Tracing.LogEventId.ArrayIndexOufOfRange })]
        public void ValidateMalformedLazys(string expression, CoreLogEventId.LogEventId[] eventIds)
        {
            var config = Build(additionalDependencies: $"[{{dependencies:[{{expression: \"{expression}\"}}], dependents: ['@ms/project-A']}}]", addDScriptResolver: true)
                .AddJavaScriptProject("@ms/project-A", "src/A")
                .AddSpec("module.config.dsc", "module({name: 'myModule'});")
                .AddSpec("@@public export const myFile = f`aFile`;")
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
            });

            Assert.False(result.IsSuccess);
            foreach(var eventId in eventIds)
            {
                AllowErrorEventLoggedAtLeastOnce(eventId);
            }
        }

        [Theory]
        [InlineData("[{dependencies:undefined, dependents: []}]")]
        [InlineData("[{dependencies:[], dependents: undefined}]")]
        public void ValidateMalformedAdditionalDependencies(string additionalDependencies)
        {
            var config = Build(additionalDependencies: additionalDependencies)
                .AddJavaScriptProject("@ms/project-A", "src/A")
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
            });

            Assert.False(result.IsSuccess);
            AssertErrorEventLogged(LogEventId.InvalidResolverSettings);
        }

        [Theory]
        [InlineData("[{dependencies:[{packageNameRegex: '['}], dependents: ['@ms/project-A']}]")]
        [InlineData("[{dependencies:['@ms/project-A'], dependents: [{packageNameRegex: '['}]}]")]
        [InlineData("[{dependencies:['@ms/project-A'], dependents: [{packageNameRegex: '.*', commandRegex: '['}]}]")]
        public void ValidateMalformedRegex(string additionalDependencies)
        {
            var config = Build(additionalDependencies: additionalDependencies)
                .AddJavaScriptProject("@ms/project-A", "src/A")
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
            });

            Assert.False(result.IsSuccess);
            AssertErrorEventLogged(LogEventId.InvalidRegexInProjectSelector);
            AssertErrorEventLogged(CoreLogEventId.LogEventId.CannotBuildWorkspace);
        }
    }
}

