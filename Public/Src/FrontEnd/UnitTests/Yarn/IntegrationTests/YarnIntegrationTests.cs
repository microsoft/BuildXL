// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using BuildXL.Pips.Operations;
using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.FrontEnd.Yarn;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Yarn
{
    /// <summary>
    /// End to end execution tests for Yarn, including pip execution
    /// </summary>
    /// <remarks>
    /// The common JavaScript functionality is already tested in the Rush related tests, so we don't duplicate it here.
    /// </remarks>
    public class YarnIntegrationTests : YarnIntegrationTestBase
    {
        public YarnIntegrationTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void EndToEndPipExecutionWithDependencies()
        {
            // Create two projects A and B such that A -> B.
            var config = Build()
                .AddJavaScriptProject("@ms/project-A", "src/A", "module.exports = function A(){}")
                .AddJavaScriptProject("@ms/project-B", "src/B", "const A = require('@ms/project-A'); return A();", new string[] { "@ms/project-A"})
                .PersistSpecsAndGetConfiguration();

            var engineResult = RunYarnProjects(config);

            Assert.True(engineResult.IsSuccess);
            
            // Let's do some basic graph validations
            var processes = engineResult.EngineState.PipGraph.RetrievePipsOfType(PipType.Process).ToList();
            // There should be two process pips
            Assert.Equal(2, processes.Count);

            // Project A depends on project B
            var projectAPip = engineResult.EngineState.RetrieveProcess("_ms_project_A");
            var projectBPip = engineResult.EngineState.RetrieveProcess("_ms_project_B");
            Assert.True(IsDependencyAndDependent(projectAPip, projectBPip));
        }

        /// <summary>
        /// When retrieving the build graph, 'yarn workspaces' should be called using as the working directory the root configured at the resolver level, since 
        /// that determines what the tool retrieves
        /// </summary>
        /// <remarks>
        /// Observe this is not required for Rush, since rush-lib takes the target directory as an explicit argument. That's the reason why this test is here and not under the Rush tests,
        /// where most of the tests are.
        /// </remarks>
        [Fact]
        public void ResolverRootIsHonored()
        {
            // Create a project A at the root. The resulting config is not used, we call this as a handy way to persist a Yarn project
            Build()
                .AddJavaScriptProject("@ms/project-A", "src/A")
                .PersistSpecsAndGetConfiguration();

            // Create a project B in a nested location and create a config whose root points to it
            var configForNested = Build(root: "d`nested`")
                .AddJavaScriptProject("@ms/project-B", "nested/src/B")
                .PersistSpecsAndGetConfiguration();

            // Initializes a yarn repo at the root
            if (!YarnInit(configForNested.Layout.SourceDirectory))
            {
                throw new InvalidOperationException("Yarn init failed.");
            }

            // Initializes a yarn repo at the nested location
            if (!YarnInit(configForNested.Layout.SourceDirectory.Combine(PathTable, "nested")))
            {
                throw new InvalidOperationException("Yarn init failed.");
            }

            // At this point we should have two Yarn workspaces created, one at the root and another one under 'nested'
            Assert.True(File.Exists(configForNested.Layout.SourceDirectory.Combine(PathTable, "yarn.lock").ToString(PathTable)));
            Assert.True(File.Exists(configForNested.Layout.SourceDirectory.Combine(PathTable, "nested").Combine(PathTable, "yarn.lock").ToString(PathTable)));

            // Run the config for nested, whose root should point to 'nested' folder
            var engineResult = RunEngine(configForNested);

            Assert.True(engineResult.IsSuccess);

            var processes = engineResult.EngineState.PipGraph.RetrievePipsOfType(PipType.Process).ToList();
            // There should be one process pip
            Assert.Equal(1, processes.Count);

            // The project should be B
            var projectBPip = engineResult.EngineState.RetrieveProcess("_ms_project_B");
            Assert.NotNull(projectBPip);
        }
    }
}
