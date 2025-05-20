// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Lage.IntegrationTests
{
    public class LageSourceDependenciesTests : LageIntegrationTestBase
    {
        /// <summary>
        /// Point to the mock version of lage, so we get source dependency behavior.
        /// </summary>
        /// <remarks>
        /// Keep in sync with deployment.
        /// </remarks>
        protected override string PathToLageFolder => Path.Combine(TestDeploymentDir, "lage-mock").Replace("\\", "/");

        /// <summary>
        /// <see cref="PathToLageFolder"/>
        /// </summary>
        protected override string PathToLage => Path.Combine(TestDeploymentDir, "lage-mock", OperatingSystemHelper.IsWindowsOS ? "lage.exe" : "lage").Replace("\\", "/");

        /// <summary>
        /// Run up to schedule
        /// </summary>
        protected override EnginePhases Phase => EnginePhases.Schedule;

        public LageSourceDependenciesTests(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestSourceDependencies(bool enforceSourceReadsUnderPackageRoots)
        {
            var escapedRelativeSourceRoot = RelativeSourceRoot.Replace(@"\", @"\\\\");

            // Create two projects A and B such that A -> B. Set up some arbitrary source directories for each project.
            var config = Build(
                enforceSourceReadsUnderPackageRoots: enforceSourceReadsUnderPackageRoots,
                dependencies: ["A -> B"],
                sourceDirectories: [
                    $"A -> {escapedRelativeSourceRoot}/foo",
                    $"A -> {escapedRelativeSourceRoot}/bar",
                    $"B -> {escapedRelativeSourceRoot}/baz",
                ])
                .PersistSpecsAndGetConfiguration();

            var result = RunLageProjects(config);
            Assert.True(result.IsSuccess);

            Assert.Equal(2, result.EngineState.RetrieveProcesses().Count());

            var projectA = result.EngineState.RetrieveProcess("A", "A");
            var projectB = result.EngineState.RetrieveProcess("B", "B");

            // Check that the source directories are set up correctly for each project. This should only happen if enforceSourceReadsUnderPackageRoots is true.
            if (enforceSourceReadsUnderPackageRoots)
            {
                Assert.Contains("foo", projectA.AllowedUndeclaredSourceReadScopes.Select(path => path.GetName(PathTable).ToString(StringTable)));
                Assert.Contains("bar", projectA.AllowedUndeclaredSourceReadScopes.Select(path => path.GetName(PathTable).ToString(StringTable)));
                Assert.Contains("baz", projectB.AllowedUndeclaredSourceReadScopes.Select(path => path.GetName(PathTable).ToString(StringTable)));
            }
            else
            {
                Assert.Empty(projectA.AllowedUndeclaredSourceReadScopes);
                Assert.Empty(projectB.AllowedUndeclaredSourceReadScopes);
            }
        }

        protected SpecEvaluationBuilder Build(
            string[] dependencies,
            string[] sourceDirectories,
            bool? enforceSourceReadsUnderPackageRoots = null)
        {
            var environment = new Dictionary<string, DiscriminatingUnion<string, UnitValue>>
            {
                ["PATH"] = new DiscriminatingUnion<string, UnitValue>(PathToNodeFolder),
                ["NODE_PATH"] = new DiscriminatingUnion<string, UnitValue>(PathToNodeFolder),
                ["LAGE_BUILD_GRAPH_MOCK_NODES"] = new DiscriminatingUnion<string, UnitValue>(string.Join(",", dependencies)),
                ["LAGE_BUILD_GRAPH_MOCK_SOURCE_DIRECTORIES"] = new DiscriminatingUnion<string, UnitValue>(string.Join(",", sourceDirectories)),
            };

            return Build(environment: environment, enforceSourceReadsUnderPackageRoots: enforceSourceReadsUnderPackageRoots);
        }
    }
}