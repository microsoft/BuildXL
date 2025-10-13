// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using BuildXL.FrontEnd.Nx;
using BuildXL.FrontEnd.Script;
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
    /// Scheduling tests for Nx
    /// </summary>
    /// <remarks>
    /// The common JavaScript functionality is already tested in the Rush related tests, so we don't duplicate it here.
    /// </remarks>
    public class NxSchedulingTests : NxIntegrationTestBase
    {
        public NxSchedulingTests(ITestOutputHelper output)
            : base(output)
        {
        }

        /// <summary>
        /// Run up to schedule phase
        /// </summary>
        protected override EnginePhases Phase => EnginePhases.Schedule;

        [FactIfSupported(requiresLinuxBasedOperatingSystem: true)]
        public void TagsAreHonored()
        {
            // Create a project A
            var config = Build()
                .AddJavaScriptProject("@ms/project-A", "src/A", "function A(){}")
                .PersistSpecsAndGetConfiguration();

            // Add a project.json with tags to project A, so we can later check that they got reflected in the pip tags
            File.WriteAllText(config.Layout.SourceDirectory.Combine(PathTable, RelativePath.Create(StringTable, "src/A/project.json")).ToString(PathTable), @"
                { 
                    ""name"": ""project-A"",
                    ""tags"": [""custom-tag-1"", ""custom-tag-2""],
                }"
            );

            var engineResult = RunNxProjects(config);
            Assert.True(engineResult.IsSuccess);

            // There should be one process pip with the tags from project.json and the build tag
            var process = engineResult.EngineState.PipGraph.RetrievePipsOfType(PipType.Process).Single();
            var tags = process.Tags.Select(tag => tag.ToString(StringTable)).ToList();

            Assert.Contains("build", tags);
            Assert.Contains("custom-tag-1", tags);
            Assert.Contains("custom-tag-2", tags);
        }
    }
}
