// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Configuration;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Rush.IntegrationTests
{
    [Trait("Category", "RushIntegrationTests")]
    public class RushEnforceUndeclaredReadsTest : RushIntegrationTestBase
    {
        public RushEnforceUndeclaredReadsTest(ITestOutputHelper output)
            : base(output)
        {
        }

        // We need to execute in order to verify the presence of DFAs
        protected override EnginePhases Phase => EnginePhases.Execute;

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ReadOutOfAllowedUndeclaredReadsConeIsFlagged(bool declareDependency)
        {
            // On CB there is a reparse point involved in the read scopes, so turn on reparse point resolving so scopes consider reparse points
            var config = Build(enforceSourceReadsUnderPackageRoots: true, enableFullReparsePointResolving: true)
                // Package A access its own project root cone
                .AddJavaScriptProject("@ms/project-A", "src/A", "const fs = require('fs'); fs.existsSync('A.txt');")
                // Package B access A's sources
                .AddJavaScriptProject("@ms/project-B", "src/B", "const fs = require('fs'); fs.existsSync('../A/A.txt');", declareDependency? new string[] { "@ms/project-A" } : null)
                .AddSpec("src/A/A.txt", "A source file")
                .PersistSpecsAndGetConfiguration();

            var engineResult = RunRushProjects(
                config, 
                new[] {
                    ("src/A", "@ms/project-A"),
                    ("src/B", "@ms/project-B"),
                }, 
                overrideDisableReparsePointResolution: false);

            if (declareDependency)
            {
                Assert.True(engineResult.IsSuccess);
            }
            else
            {
                Assert.False(engineResult.IsSuccess);
                AssertErrorEventLogged(global::BuildXL.Scheduler.Tracing.LogEventId.DependencyViolationDisallowedUndeclaredSourceRead);
            }
        }
    }
}
