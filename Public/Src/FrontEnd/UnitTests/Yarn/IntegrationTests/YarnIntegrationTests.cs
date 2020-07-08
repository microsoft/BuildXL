// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
    }
}
