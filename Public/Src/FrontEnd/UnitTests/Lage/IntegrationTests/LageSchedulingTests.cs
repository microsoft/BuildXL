// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Lage
{
    /// <summary>
    /// End to end scheduling execution tests for Lage
    /// </summary>
    /// <remarks>
    /// The common JavaScript functionality is already tested in the Rush related tests, so we don't duplicate it here.
    /// </remarks>
    public class LageSchedulingTests : LageIntegrationTestBase
    {
        public LageSchedulingTests(ITestOutputHelper output)
            : base(output)
        {
        }

        /// <summary>
        /// Run up to schedule
        /// </summary>
        protected override EnginePhases Phase => EnginePhases.Schedule;

        [Fact]
        public void EndToEndPipSchedulingWithDependencies()
        {
            // Create two projects A and B such that A -> B.
            var config = Build()
                .AddJavaScriptProject("@ms/project-A", "src/A", "module.exports = function A(){}")
                .AddJavaScriptProject("@ms/project-B", "src/B", "const A = require('@ms/project-A'); return A();", new string[] { "@ms/project-A"})
                .PersistSpecsAndGetConfiguration();

            var engineResult = RunLageProjects(config);

            Assert.True(engineResult.IsSuccess);
            
            // Let's do some basic graph validations
            var processes = engineResult.EngineState.PipGraph.RetrievePipsOfType(PipType.Process).ToList();
            // There should be two process pips
            Assert.Equal(2, processes.Count);

            // Project A depends on project B
            var projectAPip = engineResult.EngineState.RetrieveProcess("_ms_project_A_build");
            var projectBPip = engineResult.EngineState.RetrieveProcess("_ms_project_B_build");
            Assert.True(IsDependencyAndDependent(projectAPip, projectBPip));
        }

        [Fact]
        public void NonExistentScriptInDependencyIsIgnored()
        {
            // Create two projects A and B such that A -> B.
            var config = Build(executeCommands: new[] { "build", "test"})
                .AddJavaScriptProject("@ms/project-A", "src/A", "module.exports = function A(){}", scriptCommands: new[] { ("test", "node ./main.js") })
                .AddJavaScriptProject("@ms/project-B", "src/B", "const A = require('@ms/project-A'); return A();", new string[] { "@ms/project-A" }, scriptCommands: new[] { 
                    ("test", "node ./main.js"),
                    ("build", "node ./main.js")
                })
                .PersistSpecsAndGetConfiguration();

            var engineResult = RunLageProjects(config);

            Assert.True(engineResult.IsSuccess);

            var processes = engineResult.EngineState.PipGraph.RetrievePipsOfType(PipType.Process).ToList();
            // There should be three process pips
            Assert.Equal(3, processes.Count);

            // B#build -> A#build and A#test -> A#build are reported by Lage without A#build being defined. We just ignore those but log them.
            AssertVerboseEventLogged(global::BuildXL.FrontEnd.JavaScript.Tracing.LogEventId.IgnoredDependency, count: 2);
        }
    }
}
