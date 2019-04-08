// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Xunit;

namespace Microsoft.Build.Prediction.Tests
{
    /// <summary>
    /// Base class that provides helper methods for test code, including
    /// interfacing with sample files in the TestsData folder.
    /// </summary>
    public abstract class TestBase
    {
        static TestBase()
        {
            MsBuildEnvironment.Setup(s_assemblyLocation);
        }

        private static string s_assemblyLocation = TestHelpers.GetAssemblyLocation();

        /// <summary>
        /// Gets the relative path for resource files used by the test suite.
        /// The path is relative to the BuildPredictionTests output folder.
        /// This is typically something like @"TestsData\Xxx" where Xxx is the
        /// test suite type.
        /// </summary>
        protected abstract string TestsDirectoryPath { get; }

        /// <summary>
        /// Creates an absolute path using the assembly location as the root, followed by <see cref="TestsDirectoryPath"/>
        /// </summary>
        protected string CreateAbsolutePath(string relativePath)
        {
            return Path.Combine(s_assemblyLocation, TestsDirectoryPath, relativePath);
        }

        protected void ParseAndVerifyProject(
            string projFileName,
            IProjectStaticPredictor predictor,
            IReadOnlyCollection<BuildInput> expectedInputs,
            IReadOnlyCollection<BuildOutputDirectory> expectedOutputs)
        {
            var projectCollection = new ProjectCollection();
            var project = new Project(
                Path.Combine(TestsDirectoryPath, projFileName),  // TestsData files are marked to CopyToOutput and are available next to the executing assembly
                new Dictionary<string, string>
                {
                    { "Platform", "amd64" },
                    { "Configuration", "debug" },
                },
                null,
                projectCollection);
            ProjectInstance projectInstance = project.CreateProjectInstance(ProjectInstanceSettings.ImmutableWithFastItemLookup);

            bool success = predictor.TryPredictInputsAndOutputs(
                project,
                projectInstance,
                TestsDirectoryPath,
                out StaticPredictions predictions);


            IReadOnlyCollection<BuildInput> absolutePathInputs = expectedInputs.Select(i => 
                new BuildInput(Path.Combine(project.DirectoryPath, i.Path), i.IsDirectory))
                    .ToList();
            Assert.True(success, "Prediction returned false (no predictions)");
            predictions.AssertPredictions(absolutePathInputs, expectedOutputs);
        }
    }
}
