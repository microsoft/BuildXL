// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
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
            var config = Build(executeCommands: "['build', 'test']")
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

        [Fact]
        public void SinceIsHonored()
        {
            var since = "dev/feature";
            // Create a project A
            var config = Build(executeCommands: "['test']", since: since)
                .AddJavaScriptProject("@ms/project-A", "src/A", "module.exports = function A(){}", scriptCommands: new[] { ("test", "node ./main.js") })
                .PersistSpecsAndGetConfiguration();

            RunLageProjects(config);

            // It it pretty hard to come up with an e2e test for this, since it involves git operations and having a consistent change across git branches/commits that would make sense to Lage
            // Check instead that the 'since' argument was actually passed to the graph construction tool
            AssertVerboseEventLogged(global::BuildXL.FrontEnd.JavaScript.Tracing.LogEventId.ConstructingGraphScript, count: 1);
            string graphConstructionToolArgs = EventListener.GetLogMessagesForEventId((int)global::BuildXL.FrontEnd.JavaScript.Tracing.LogEventId.ConstructingGraphScript).Single();

            Assert.Contains($"{since}", graphConstructionToolArgs);

            // Lage sometimes fails with an obscure error because the underlying git operation fails (depending on the environment where it runs)
            AllowErrorEventMaybeLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CannotBuildWorkspace);
            AllowErrorEventMaybeLogged(global::BuildXL.FrontEnd.JavaScript.Tracing.LogEventId.ProjectGraphConstructionError);
        }

        [Fact]
        public void LageLocationIsHonored()
        {
            var config = Build(executeCommands: "['test']", lageLocation: PathToLage)
                .AddJavaScriptProject("@ms/project-A", "src/A", "module.exports = function A(){}", scriptCommands: new[] { ("test", "node ./main.js") })
                .PersistSpecsAndGetConfiguration();

            var engineResult = RunLageProjects(config);

            Assert.True(engineResult.IsSuccess);

            AssertVerboseEventLogged(global::BuildXL.FrontEnd.JavaScript.Tracing.LogEventId.ConstructingGraphScript, count: 1);
            string graphConstructionToolArgs = EventListener.GetLogMessagesForEventId((int)global::BuildXL.FrontEnd.JavaScript.Tracing.LogEventId.ConstructingGraphScript).Single();

            // We should be passing 'undefined' for the npm location (4th arg) and the path to lage as the lage location (6th arg)
            // Nothing special about windows here wrt behavior, but for the Linux case the equivalent command line has a bunch
            // of escaped quotes, so it would just make the test less readable.
            if (OperatingSystemHelper.IsWindowsOS)
            {
                Assert.Contains($@"""undefined"" ""test"" ""{PathToLage}""", graphConstructionToolArgs);
            }

            // The graph construction process should be returning a single process pip.
            var processes = engineResult.EngineState.PipGraph.RetrievePipsOfType(PipType.Process).ToList();
            Assert.Equal(1, processes.Count);
        }
    }
}
