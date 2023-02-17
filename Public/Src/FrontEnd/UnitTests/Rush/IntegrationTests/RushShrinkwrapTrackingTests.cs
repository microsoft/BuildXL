// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Rush
{
    [Trait("Category", "RushExecuteTests")]
    public class RushShrinkwrapTrackingTests : RushIntegrationTestBase
    {
        public RushShrinkwrapTrackingTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // We don't actually need to execute anything, scheduling is enough
        protected override EnginePhases Phase => EnginePhases.Schedule;

        [Fact]
        public void ShrinkwrapDepsIsUsedInsteadOfRealDependencies()
        {
            string commonTempFolder = Path.Combine(TestRoot, "CustomTempFolder").Replace("\\", "/"); ;

            var config = Build(commonTempFolder: commonTempFolder)
                .AddJavaScriptProject("@ms/project-A", "src/A").
                PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.True(result.IsSuccess);

            // The pip should have the common temp folder untracked, and shrinkwrap-deps.json 
            // should be a declared input
            var pipA = result.EngineState.RetrieveProcess("@ms/project-A", "build");
            XAssert.Contains(pipA.UntrackedScopes, CreatePath(commonTempFolder));
            XAssert.Contains(
                pipA.Dependencies.Select(fa => fa.Path), 
                pipA.WorkingDirectory.Combine(PathTable, RelativePath.Create(StringTable, ".rush/temp/shrinkwrap-deps.json")));
        }
    }
}
