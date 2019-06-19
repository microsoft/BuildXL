// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.FrontEnd.MsBuild.Serialization;
using MsBuildGraphBuilderTool;
using Test.BuildXL.TestUtilities.Xunit;
using Test.ProjectGraphBuilder.Mocks;
using Test.ProjectGraphBuilder.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Test.ProjectGraphBuilder
{
    /// <summary>
    /// Tests related to the decorations associated to the project graph (e.g. failures, location of assemblies, etc.)
    /// </summary>
    public class MsBuildGraphDecorationTests : TemporaryStorageTestBase
    {
        public MsBuildGraphDecorationTests(ITestOutputHelper output): base(output)
        {
        }

        [Fact]
        public void FailureDuringAssemblyLoadingIsReflectedInTheSerializedGraph()
        {
            var projectGraphWithPredictionsResult = BuildGraphAndDeserialize(new FailedMsBuildAssemblyLoader());

            // We expect the result to have failed
            Assert.False(projectGraphWithPredictionsResult.Succeeded);
            Assert.False(string.IsNullOrEmpty(projectGraphWithPredictionsResult.Failure.Message));
        }

        [Fact]
        public void FailureDuringGraphConstructionIsReflectedInTheSerializedGraph()
        {
            // Write a malformed project
            var projectGraphWithPredictionsResult = BuildGraphAndDeserialize("<Malformed XML");

            // We expect the result to have failed
            Assert.False(projectGraphWithPredictionsResult.Succeeded);
            Assert.False(string.IsNullOrEmpty(projectGraphWithPredictionsResult.Failure.Message));
        }

        [Fact]
        public void ValidProjectFileSucceedsAndAssemblyLocationsAreSetProperly()
        {
            // Write an empty project
            string entryPoint =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<Project xmlns=""http://schemas.microsoft.com/developer/msbuild/2003""/>";

            var projectGraphWithPredictionsResult = BuildGraphAndDeserialize(entryPoint);

            // We expect the result to succeed
            Assert.True(projectGraphWithPredictionsResult.Succeeded);
            // The locations for MSBuild.exe and its assemblies should be properly set
            Assert.Contains(TestDeploymentDir, projectGraphWithPredictionsResult.PathToMsBuildExe);
            Assert.All(projectGraphWithPredictionsResult.MsBuildAssemblyPaths.Values, assemblyPath => assemblyPath.Contains(TestDeploymentDir));
        }

        private ProjectGraphWithPredictionsResult<string> BuildGraphAndDeserialize(string projectEntryPointContent = null)
        {
            return BuildGraphAndDeserialize(MsBuildAssemblyLoader.Instance, projectEntryPointContent);
        }

        private ProjectGraphWithPredictionsResult<string> BuildGraphAndDeserialize(IMsBuildAssemblyLoader assemblyLoader, string projectEntryPointContent = null)
        {
            string outputFile = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());
            string entryPoint = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());

            if (projectEntryPointContent != null)
            {
                File.WriteAllText(entryPoint, projectEntryPointContent);
            }

            using (var reporter = new GraphBuilderReporter(Guid.NewGuid().ToString()))
            {
                var arguments = new MSBuildGraphBuilderArguments(
                    new[] { entryPoint },
                    outputFile,
                    globalProperties: GlobalProperties.Empty,
                    mSBuildSearchLocations: new string[] {TestDeploymentDir},
                    entryPointTargets: new string[0],
                    requestedQualifiers: new GlobalProperties[] { GlobalProperties.Empty},
                    allowProjectsWithoutTargetProtocol: false);

                MsBuildGraphBuilder.BuildGraphAndSerializeForTesting(assemblyLoader, reporter, arguments);
            }

            // The serialized graph should exist
            Assert.True(File.Exists(outputFile));

            var projectGraphWithPredictionsResult = SimpleDeserializer.Instance.DeserializeGraph(outputFile);

            return projectGraphWithPredictionsResult;
        }
    }
}