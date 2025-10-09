// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using BuildXL.FrontEnd.Nx;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using Process = BuildXL.Pips.Operations.Process;

namespace Test.BuildXL.FrontEnd.Nx
{
    /// <summary>
    /// End to end execution tests for Nx
    /// </summary>
    /// <remarks>
    /// The common JavaScript functionality is already tested in the Rush related tests, so we don't duplicate it here.
    /// </remarks>
    public class NxIntegrationTests : NxIntegrationTestBase
    {
        public NxIntegrationTests(ITestOutputHelper output)
            : base(output)
        {
        }

        /// <summary>
        /// Run up to execution phase
        /// </summary>
        protected override EnginePhases Phase => EnginePhases.Execute;

        /// <summary>
        /// End to end test for pip scheduling with dependencies
        /// </summary>
        /// <remarks>
        /// For now this is only enabled on Linux. TODO: enable for Windows as well, nothing should be Linux specific here.
        /// </remarks>
        [FactIfSupported(requiresLinuxBasedOperatingSystem: true)]
        public void EndToEndPipSchedulingWithDependencies()
        {
            // Create two projects A and B such that A -> B.
            var config = Build()
                .AddJavaScriptProject("@ms/project-A", "src/A", "function A(){}")
                .AddJavaScriptProject("@ms/project-B", "src/B", "function B(){}", new string[] { "@ms/project-A" })
                .PersistSpecsAndGetConfiguration();

            var engineResult = RunNxProjects(config);

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
    }
}
