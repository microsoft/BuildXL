// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.FrontEnd.MsBuild.Serialization;
using MsBuildGraphBuilderTool;
using Test.BuildXL.TestUtilities.Xunit;
using Test.ProjectGraphBuilder.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Test.ProjectGraphBuilder
{
    public class MsBuildGraphConstructionTests : TemporaryStorageTestBase
    {
        private readonly MsBuildProjectBuilder m_builder;

        public MsBuildGraphConstructionTests(ITestOutputHelper output): base(output)
        {
            m_builder = new MsBuildProjectBuilder(TemporaryDirectory);
        }

        [Theory]
        // One isolated project
        [InlineData(true, "[A]")]
        // Two isolated projects
        [InlineData(false, "[A]",
                           "B")]
        // Simple dependency
        [InlineData(true, "[A] -> B")]
        // Transitive dependency
        [InlineData(true, "[A] -> B -> C",
                          "A -> C")]
        // Fan-out test
        [InlineData(true, "[A] -> B",
                          "A -> C")]
        // Fan-in test
        [InlineData(false, "[A] -> B",
                           "C -> B")]
        // Diamond test
        [InlineData(true, "[A] -> B1 -> C",
                          "A -> B2 -> C")]
        // Two connected components
        [InlineData(false, "A -> B",
                           "[C] -> D")]
        public void ValidateRoundtripProjects(bool exactMatch, params string[] projectChains)
        {
            // Write to disk the set of projects that the project chains represent
            var entryPointPath = m_builder.WriteProjectsWithReferences(projectChains);

            // Parse the projects, build the graph, serialize it to disk and deserialize it back
            var projectGraphWithPredictionsResult = BuildGraphAndDeserialize(new[] { entryPointPath });

            Assert.True(projectGraphWithPredictionsResult.Succeeded);

            // Validate the graph matches (or is a subset) of the original project chains
            m_builder.ValidateGraphIsSubgraphOfChains(projectGraphWithPredictionsResult.Result, exactMatch, projectChains);
        }

        [Fact]
        public void ReferencesAreTreatedAsASet()
        {
            // We create a project that references the same inner project twice
            const string DoubleReferenceProject =
@"
<Project>
    <PropertyGroup>
       <InnerBuildProperty>InnerBuild</InnerBuildProperty>
       <InnerBuildPropertyValues>InnerBuildProperties</InnerBuildPropertyValues>
       <InnerBuildProperties>A;A</InnerBuildProperties>
    </PropertyGroup>
</Project>";

            var entryPointPath = m_builder.WriteProjectsWithReferences(("A", DoubleReferenceProject));

            // Parse the projects, build the graph, serialize it to disk and deserialize it back
            var projectGraphWithPredictionsResult = BuildGraphAndDeserialize(new[] { entryPointPath });

            Assert.True(projectGraphWithPredictionsResult.Succeeded);

            // There should be two projects: the outer and the inner ones
            var projectNodes = projectGraphWithPredictionsResult.Result.ProjectNodes;
            Assert.Equal(2, projectNodes.Length);

            // The outer project has just one global property (IsGraphBuild: true)
            var outerProject = projectNodes.First(project => project.GlobalProperties.Count == 1);
            var innerProject = projectNodes.First(project => project.GlobalProperties.Count == 2);

            // There should be only a single reference from the outer project to the inner one
            Assert.Equal(1, outerProject.ProjectReferences.Count);
            // And the inner one should have no references
            Assert.Equal(0, innerProject.ProjectReferences.Count);
        }

        [Fact]
        public void QualifiersAndGlobalPropertiesShouldAgree()
        {
            var entryPointPath = m_builder.WriteProjectsWithReferences(("A", "<Project/>"));

            var requestedQualifiers = new GlobalProperties[] { new GlobalProperties(new Dictionary<string, string> { ["platform"] = "x64" }) };
            var globalProperties = new GlobalProperties(new Dictionary<string, string> { ["platform"] = "amd64" });

            var arguments = CreateBuilderArguments(entryPointPath, requestedQualifiers, globalProperties);

            var projectGraphWithPredictionsResult = BuildGraphAndDeserialize(arguments);

            Assert.False(projectGraphWithPredictionsResult.Succeeded);
            Assert.Contains("the specified values for 'PLATFORM' do not agree", projectGraphWithPredictionsResult.Failure.Message);
        }

        [Fact]
        public void QualifiersAndGlobalPropertiesAreMerged()
        {
            var entryPointPath = m_builder.WriteProjectsWithReferences(("A", "<Project/>"));

            var requestedQualifiers = new GlobalProperties[] { new GlobalProperties(new Dictionary<string, string> { ["platform"] = "x64" }) };
            var globalProperties = new GlobalProperties(new Dictionary<string, string> { ["configuration"] = "release" });

            var arguments = CreateBuilderArguments(entryPointPath, requestedQualifiers, globalProperties);

            var projectGraphWithPredictionsResult = BuildGraphAndDeserialize(arguments);

            Assert.True(projectGraphWithPredictionsResult.Succeeded);
            var projectProperties = projectGraphWithPredictionsResult.Result.ProjectNodes.Single().GlobalProperties;

            Assert.Equal("x64", projectProperties["platform"]);
            Assert.Equal("release", projectProperties["configuration"]);
        }

        [Fact]
        public void QualifiersAndGlobalPropertiesAreMergedPerQualifier()
        {
            var entryPointPath = m_builder.WriteProjectsWithReferences(("A", "<Project/>"));

            // let's 'build' for debug and release
            var requestedQualifiers = new GlobalProperties[] {
                new GlobalProperties(new Dictionary<string, string> { ["configuration"] = "debug" }),
                new GlobalProperties(new Dictionary<string, string> { ["configuration"] = "release" }),
            };

            var globalProperties = new GlobalProperties(new Dictionary<string, string> { ["platform"] = "x86" });

            var arguments = CreateBuilderArguments(entryPointPath, requestedQualifiers, globalProperties);

            var projectGraphWithPredictionsResult = BuildGraphAndDeserialize(arguments);

            Assert.True(projectGraphWithPredictionsResult.Succeeded);
            var nodes = projectGraphWithPredictionsResult.Result.ProjectNodes;

            // There should be two nodes, one per qualifier
            Assert.Equal(2, nodes.Count());
            var debugNode = nodes.First(node => node.GlobalProperties["configuration"] == "debug");
            var releaseNode = nodes.First(node => node.GlobalProperties["configuration"] == "release");

            // Both nodes should have the same platform, since that's part of the global properties
            Assert.All(nodes, node => Assert.Equal("x86", node.GlobalProperties["platform"]));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void NonLeafProjectNotImplementingProtocolFailsOnConstructionBasedOnConfiguration(bool allowProjectsWithoutTargetProtocol)
        {
            var entryPointPath = m_builder.WriteProjectsWithReferences("[A.proj] -> B.proj");

            var arguments = CreateBuilderArguments(entryPointPath, allowProjectsWithoutTargetProtocol: allowProjectsWithoutTargetProtocol);

            var projectGraphWithPredictionsResult = BuildGraphAndDeserialize(arguments);

            if (allowProjectsWithoutTargetProtocol)
            {
                // If projects are allowed to not implement the protocol, then everything should succeed
                Assert.True(projectGraphWithPredictionsResult.Succeeded);
            }
            else
            {
                // Otherwise, there should be a failure involving A.proj (the non-leaf project)
                Assert.False(projectGraphWithPredictionsResult.Succeeded);
                Assert.Contains("A.proj", projectGraphWithPredictionsResult.Failure.Message);
            }
        }

        [Fact]
        public void DefaultTargetsAreAppendedWhenDependencyDoesNotImplementProtocol()
        {
            // Project A does not implement the target protocol and references B
            // Project B default targets are Build and Pack
            var entryPointPath = m_builder.WriteProjectsWithReferences(
                ("A.proj", @"
<Project>
  <ItemGroup>
    <ProjectReference Include='B.proj'/>
  </ItemGroup>
  <Target Name='Build'/>
</Project>"),
                ("B.proj", @"
<Project DefaultTargets='Build;Pack'>
  <Target Name='Build'/>
  <Target Name='Pack'/>
</Project>"));

            // The only way in which the above situation is allowed is if we allow projects without target protocol
            var arguments = CreateBuilderArguments(entryPointPath, allowProjectsWithoutTargetProtocol: true);

            var projectGraphWithPredictionsResult = BuildGraphAndDeserialize(arguments);

            Assert.True(projectGraphWithPredictionsResult.Succeeded);
            var projectB = projectGraphWithPredictionsResult.Result.ProjectNodes.First(projectNode => projectNode.FullPath.Contains("B.proj"));

            // The targets of B should be flagged so we know default targets were appended,
            // and B targets should contain the default targets
            Assert.True(projectB.PredictedTargetsToExecute.IsDefaultTargetsAppended);
            Assert.Contains("Build", projectB.PredictedTargetsToExecute.Targets);
            Assert.Contains("Pack", projectB.PredictedTargetsToExecute.Targets);
        }

        private ProjectGraphWithPredictionsResult<string> BuildGraphAndDeserialize(MSBuildGraphBuilderArguments arguments)
        {
            MsBuildGraphBuilder.BuildGraphAndSerialize(arguments);

            // The serialized graph should exist
            Assert.True(File.Exists(arguments.OutputPath));

            var projectGraphWithPredictionsResult = SimpleDeserializer.Instance.DeserializeGraph(arguments.OutputPath);

            return projectGraphWithPredictionsResult;
        }

        private ProjectGraphWithPredictionsResult<string> BuildGraphAndDeserialize(IReadOnlyCollection<string> projectEntryPoints)
        {
            string outputFile = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());

            var arguments = new MSBuildGraphBuilderArguments(
                    projectEntryPoints,
                    outputFile,
                    globalProperties: GlobalProperties.Empty,
                    mSBuildSearchLocations: new[] {TestDeploymentDir},
                    entryPointTargets: new string[0],
                    requestedQualifiers: new GlobalProperties[] { GlobalProperties.Empty },
                    allowProjectsWithoutTargetProtocol: true);

            return BuildGraphAndDeserialize(arguments);

        }

        private MSBuildGraphBuilderArguments CreateBuilderArguments(string entryPointPath, GlobalProperties[] requestedQualifiers = null, GlobalProperties globalProperties = null, bool allowProjectsWithoutTargetProtocol = true)
        {
            string outputFile = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());
            var arguments = new MSBuildGraphBuilderArguments(
                    new string[] { entryPointPath },
                    outputFile,
                    globalProperties: globalProperties ?? GlobalProperties.Empty,
                    mSBuildSearchLocations: new[] { TestDeploymentDir },
                    entryPointTargets: new string[0],
                    requestedQualifiers: requestedQualifiers ?? new GlobalProperties[] { GlobalProperties.Empty },
                    allowProjectsWithoutTargetProtocol: allowProjectsWithoutTargetProtocol);
            return arguments;
        }
    }
}