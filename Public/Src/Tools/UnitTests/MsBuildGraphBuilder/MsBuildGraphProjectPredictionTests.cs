// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Build.Prediction;
using BuildXL.FrontEnd.MsBuild.Serialization;
using MsBuildGraphBuilderTool;
using Test.BuildXL.TestUtilities.Xunit;
using Test.ProjectGraphBuilder.Utilities;
using Test.Tool.ProjectGraphBuilder.Mocks;
using Xunit;
using Xunit.Abstractions;

namespace Test.ProjectGraphBuilder
{
    /// <summary>
    /// Makes sure that project predictions are plumbed through and serialized into the project graph. The actual predictions are not tested here.
    /// </summary>
    public class MsBuildGraphProjectPredictionTests : GraphBuilderToolTestBase
    {
        public MsBuildGraphProjectPredictionTests(ITestOutputHelper output): base(output)
        {
        }

        [Fact]
        public void ProjectPredictionsAreSerialized()
        {
            string outputFile = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());
            string entryPoint = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());

            // Here we create a project that we know standard predictors are able to predict: a compile item and an 'OutDir' property, that the CSharp and OutDir predictors should catch
            File.WriteAllText(entryPoint,
@"<Project xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
    <PropertyGroup>
        <OutDir>bin</OutDir>
    </PropertyGroup>
    <ItemGroup>
        <Compile Include=""Program.cs"" />
    </ItemGroup >
</Project>
");

            MsBuildGraphBuilder.BuildGraphAndSerialize(
                GetStandardBuilderArguments(
                    new[] { entryPoint },
                    outputFile,
                    globalProperties: GlobalProperties.Empty,
                    entryPointTargets: new string[0],
                    requestedQualifiers: new GlobalProperties[] { GlobalProperties.Empty },
                    allowProjectsWithoutTargetProtocol: false));

            var result = SimpleDeserializer.Instance.DeserializeGraph(outputFile);

            Assert.True(result.Succeeded);

            // There is a single project in this graph, which should have non-empty predictions
            ProjectWithPredictions<string> project = result.Result.ProjectNodes.Single();
            Assert.True(project.PredictedInputFiles.Count > 0, "Expected a non-empty collection of predicted input files");
            Assert.True(project.PredictedOutputFolders.Count > 0, "Expected a non-empty collection of predicted output folders");
        }

        [Fact]
        public void ProblematicPredictorsAreHandled()
        {
            string outputFile = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());
            // We don't bother creating content for the entry point project. The predictor is going to fail anyway.
            string entryPoint = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());

            using (var reporter = new GraphBuilderReporter(Guid.NewGuid().ToString()))
            {
                var arguments = GetStandardBuilderArguments(
                    new[] { entryPoint },
                    outputFile,
                    globalProperties: GlobalProperties.Empty,
                    entryPointTargets: new string[0],
                    requestedQualifiers: new GlobalProperties[] { GlobalProperties.Empty },
                    allowProjectsWithoutTargetProtocol: false);

                MsBuildGraphBuilder.BuildGraphAndSerializeForTesting(
                    AssemblyLoader,
                    reporter,
                    arguments,
                    new IProjectPredictor[] { new ThrowOnPredictionPredictor() });
            }

            var result = SimpleDeserializer.Instance.DeserializeGraph(outputFile);

            // The result should gracefully fail, with some error message.
            Assert.False(result.Succeeded);
            Assert.True(result.Failure.Message != null);
        }

        [Fact]
        public void PropertyTrackingIgnoredWhenDisabled()
        {
            string outputFile = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());
            string entryPoint = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());

            // Create an environment variable with a unique name.
            string envVarName = Guid.NewGuid().ToString().Replace("-", string.Empty);
            Environment.SetEnvironmentVariable(envVarName, "value not important");

            File.WriteAllText(entryPoint,
                "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\"><PropertyGroup><PropertyOne>$(" + envVarName + ")</PropertyOne></PropertyGroup></Project>");

            MsBuildGraphBuilder.BuildGraphAndSerialize(
                GetStandardBuilderArguments(
                    new[] { entryPoint },
                    outputFile,
                    globalProperties: GlobalProperties.Empty,
                    entryPointTargets: new string[0],
                    requestedQualifiers: new GlobalProperties[] { GlobalProperties.Empty },
                    allowProjectsWithoutTargetProtocol: false));

            // Remove the environment variable.
            Environment.SetEnvironmentVariable(envVarName, string.Empty);

            var result = SimpleDeserializer.Instance.DeserializeGraph(outputFile);
            Assert.True(result.Succeeded);

            // There should have been no tracking.
            Assert.NotNull(result.EnvironmentVariablesAffectingBuild);
            Assert.Equal(0, result.EnvironmentVariablesAffectingBuild.Count());
        }

        [Fact]
        public void PropertyTrackingWorksWhenEnabled()
        {
            string outputFile = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());
            string entryPoint = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());

            // Create an environment variable with a unique name.
            string envVarName = Guid.NewGuid().ToString().Replace("-", string.Empty);
            Environment.SetEnvironmentVariable(envVarName, "value not important");
            Environment.SetEnvironmentVariable("MsBuildLogPropertyTracking", "15"); // 15 = Log all property tracking events.

            File.WriteAllText(entryPoint,
                "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\"><PropertyGroup><PropertyOne>$(" + envVarName + ")</PropertyOne></PropertyGroup></Project>");

            MsBuildGraphBuilder.BuildGraphAndSerialize(
                GetStandardBuilderArguments(
                    new[] { entryPoint },
                    outputFile,
                    globalProperties: GlobalProperties.Empty,
                    entryPointTargets: new string[0],
                    requestedQualifiers: new GlobalProperties[] { GlobalProperties.Empty },
                    allowProjectsWithoutTargetProtocol: false));

            // Remove the environment variables.
            Environment.SetEnvironmentVariable(envVarName, string.Empty);
            Environment.SetEnvironmentVariable("MsBuildLogPropertyTracking", string.Empty);

            var result = SimpleDeserializer.Instance.DeserializeGraph(outputFile);
            Assert.True(result.Succeeded);

            // Tracking should have reported the set (and read) env var.
            Assert.NotNull(result.EnvironmentVariablesAffectingBuild);
            Assert.True(result.EnvironmentVariablesAffectingBuild.Contains(envVarName));
        }
    }
}