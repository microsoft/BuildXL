// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
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

        private ProjectGraphWithPredictionsResult<string> BuildGraphAndDeserialize(IReadOnlyCollection<string> projectEntryPoints)
        {
            string outputFile = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());

            MsBuildGraphBuilder.BuildGraphAndSerialize(
                new MSBuildGraphBuilderArguments(
                    TestOutputDirectory,
                    projectEntryPoints,
                    outputFile,
                    globalProperties: null,
                    mSBuildSearchLocations: new[] {TestDeploymentDir},
                    entryPointTargets: new string[0]));

            // The serialized graph should exist
            Assert.True(File.Exists(outputFile));

            var projectGraphWithPredictionsResult = SimpleDeserializer.Instance.DeserializeGraph(outputFile);

            return projectGraphWithPredictionsResult;
        }
    }
}