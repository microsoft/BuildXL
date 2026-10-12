// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using BuildXL.FrontEnd.MsBuild;
using BuildXL.Utilities.Core;
using Xunit;

namespace Test.BuildXL.FrontEnd.MsBuild
{
    public class MsBuildRuntimeConfigTests
    {
        private readonly PathTable m_pathTable = new PathTable();

        [Fact]
        public void RuntimeConfigComesFromFirstMsBuildLocation()
        {
            AbsolutePath first = Path("first sdk");
            AbsolutePath second = Path("second sdk");
            AbsolutePath runtimeConfig = first.Combine(m_pathTable, "MSBuild.runtimeconfig.json");
            var files = new HashSet<AbsolutePath>
            {
                first.Combine(m_pathTable, "MSBuild.dll"),
                runtimeConfig,
                second.Combine(m_pathTable, "MSBuild.dll"),
                second.Combine(m_pathTable, "MSBuild.runtimeconfig.json"),
            };

            Assert.True(MsBuildWorkspaceResolver.TryFindMsBuildRuntimeConfig(
                m_pathTable, new[] { first, second }, AbsolutePath.Invalid, files.Contains, out AbsolutePath actual, out string failure));
            Assert.Equal(runtimeConfig, actual);
            Assert.Empty(failure);
        }

        [Fact]
        public void LocationsWithoutMsBuildAreSkipped()
        {
            AbsolutePath runtime = Path("shared framework");
            AbsolutePath sdk = Path("sdk");
            AbsolutePath runtimeConfig = sdk.Combine(m_pathTable, "MSBuild.runtimeconfig.json");
            var files = new HashSet<AbsolutePath>
            {
                runtime.Combine(m_pathTable, "MSBuild.runtimeconfig.json"),
                sdk.Combine(m_pathTable, "MSBuild.dll"),
                runtimeConfig,
            };

            Assert.True(MsBuildWorkspaceResolver.TryFindMsBuildRuntimeConfig(
                m_pathTable, new[] { runtime, sdk }, AbsolutePath.Invalid, files.Contains, out AbsolutePath actual, out string failure));
            Assert.Equal(runtimeConfig, actual);
            Assert.Empty(failure);
        }

        [Fact]
        public void MissingSelectedRuntimeConfigDoesNotFallBackToAnotherToolset()
        {
            AbsolutePath first = Path("first sdk");
            AbsolutePath second = Path("second sdk");
            var files = new HashSet<AbsolutePath>
            {
                first.Combine(m_pathTable, "MSBuild.dll"),
                second.Combine(m_pathTable, "MSBuild.dll"),
                second.Combine(m_pathTable, "MSBuild.runtimeconfig.json"),
            };

            Assert.False(MsBuildWorkspaceResolver.TryFindMsBuildRuntimeConfig(
                m_pathTable, new[] { first, second }, AbsolutePath.Invalid, files.Contains, out _, out string failure));
            Assert.Contains(first.Combine(m_pathTable, "MSBuild.runtimeconfig.json").ToString(m_pathTable), failure);
        }

        [Fact]
        public void MissingMsBuildReportsSearchLocations()
        {
            AbsolutePath sdk = Path("missing sdk");
            Assert.False(MsBuildWorkspaceResolver.TryFindMsBuildRuntimeConfig(
                m_pathTable, new[] { sdk }, AbsolutePath.Invalid, _ => false, out _, out string failure));
            Assert.Contains("MSBuild.dll", failure);
            Assert.Contains(sdk.ToString(m_pathTable), failure);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void DefaultDiscoveryUsesGraphBuilderRuntimeConfig(bool configExists)
        {
            AbsolutePath runtimeConfig = Path("tool", "ProjectGraphBuilder.runtimeconfig.json");
            bool success = MsBuildWorkspaceResolver.TryFindMsBuildRuntimeConfig(
                m_pathTable, new AbsolutePath[0], runtimeConfig, path => configExists && path == runtimeConfig,
                out AbsolutePath actual, out string failure);

            Assert.Equal(configExists, success);
            Assert.Equal(runtimeConfig, actual);
            if (configExists)
            {
                Assert.Empty(failure);
            }
            else
            {
                Assert.Contains(runtimeConfig.ToString(m_pathTable), failure);
            }
        }

        [Theory]
        [InlineData(true, @"tools\MsBuildGraphBuilder\dotnetcore\ProjectGraphBuilder.exe")]
        [InlineData(false, @"tools\MsBuildGraphBuilder\net472\ProjectGraphBuilder.exe")]
        public void GraphConstructionToolPathKeepsManagedExeSuffix(bool useDotNetCore, string expected)
        {
            Assert.Equal(expected, MsBuildWorkspaceResolver.GetRelativePathToGraphConstructionTool(useDotNetCore));
        }

        [Theory]
        [InlineData("ProjectGraphBuilder.exe")]
        [InlineData("ProjectGraphBuilder.dll")]
        public void DotNetCoreArgumentsIncludeSelectedRuntimeConfig(string toolName)
        {
            string tool = Path("graph builder", toolName).ToString(m_pathTable);
            string response = Path("response files", "graph.json").ToString(m_pathTable);
            string runtimeConfig = Path("selected sdk", "MSBuild.runtimeconfig.json").ToString(m_pathTable);

            Assert.Equal(
                $"exec --runtimeconfig \"{runtimeConfig}\" \"{tool}\" \"{response}\"",
                MsBuildWorkspaceResolver.GetGraphConstructionToolArguments(true, tool, response, runtimeConfig));
        }

        [Fact]
        public void FullFrameworkArgumentsAreUnchanged()
        {
            string response = Path("response files", "graph.json").ToString(m_pathTable);
            Assert.Equal(
                $"\"{response}\"",
                MsBuildWorkspaceResolver.GetGraphConstructionToolArguments(false, null, response, null));
        }

        private AbsolutePath Path(params string[] components) =>
            AbsolutePath.Create(m_pathTable, System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.Combine(components)));
    }
}
