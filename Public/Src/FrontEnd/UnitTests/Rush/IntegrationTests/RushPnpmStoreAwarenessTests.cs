// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using BuildXL.FrontEnd.JavaScript.Tracing;
using BuildXL.Pips.Reclassification;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Rush
{
    [Trait("Category", "RushExecuteTests")]
    public class RushPnpmStoreAwarenessTests : RushIntegrationTestBase
    {
        public RushPnpmStoreAwarenessTests(ITestOutputHelper output)
            : base(output)
        {
            // The pnpm store directory must exist for the awareness check to pass.
            var pnpmStorePath = Path.Combine(RushTempFolder, "node_modules", ".pnpm");
            Directory.CreateDirectory(pnpmStorePath);
        }

        // We don't actually need to execute anything, scheduling is enough
        protected override EnginePhases Phase => EnginePhases.Schedule;

        [Theory]
        [InlineData(null, true)]   // disallowWritesUnderPnpmStore unset defaults to true when awareness is on
        [InlineData(true, true)]   // explicitly enabled
        [InlineData(false, false)] // explicitly disabled
        public void PnpmStoreAwarenessAddsReclassificationRuleAndOutputExclusion(bool? disallowWrites, bool expectOutputExclusion)
        {
            var config = Build(usePnpmStoreAwarenessTracking: true, disallowWritesUnderPnpmStore: disallowWrites)
                .AddJavaScriptProject("@ms/project-A", "src/A")
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.True(result.IsSuccess);

            var pipA = result.EngineState.RetrieveProcess("@ms/project-A", "build");

            // The pip should always have a JavaScriptPackageStoreReclassificationRule when awareness is on
            Assert.True(pipA.ReclassificationRules.Any(r => r is JavaScriptPackageStoreReclassificationRule));

            // Output directory exclusions depend on the disallowWritesUnderPnpmStore setting
            Assert.Equal(expectOutputExclusion, pipA.OutputDirectoryExclusions.Length > 0);
        }

        [Fact]
        public void PnpmStoreAwarenessAndShrinkwrapTrackingAreIncompatible()
        {
            string commonTempFolder = Path.Combine(TestRoot, "CustomTempFolder").Replace("\\", "/");

            // commonTempFolder != null implies trackDependenciesWithShrinkwrapDepsFile: true
            var config = Build(commonTempFolder: commonTempFolder, usePnpmStoreAwarenessTracking: true)
                .AddJavaScriptProject("@ms/project-A", "src/A")
                .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A")
            });

            Assert.False(result.IsSuccess);
            AssertErrorEventLogged(global::BuildXL.FrontEnd.Rush.Tracing.LogEventId.InvalidRushResolverSettings);
        }
    }
}
