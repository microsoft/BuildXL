// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Microsoft.Build.Prediction;
using Microsoft.Build.Prediction.StandardPredictors;
using Microsoft.Build.Prediction.Tests;
using Xunit;

namespace BuildPredictionTests.StandardPredictors
{
    public class ProjectFileAndImportedFilesTests : TestBase
    {
        internal const string ImportTestsDirectoryPath = @"TestsData\Import";
        internal const string NestedImportsProjectFileName = "NestedImports.csproj";

        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "testContext", Justification = "Needed for reflection")]
        static ProjectFileAndImportedFilesTests()
        {
            MsBuildEnvironment.Setup(TestHelpers.GetAssemblyLocation());
        }

        protected override string TestsDirectoryPath => ImportTestsDirectoryPath;

        [Fact]
        public void ProjectFileAndNestedImportedFilesInCsProj()
        {
            BuildInput[] expectedInputs =
            {
                new BuildInput(Path.Combine(Environment.CurrentDirectory, ImportTestsDirectoryPath, NestedImportsProjectFileName), false),
                new BuildInput(Path.Combine(Environment.CurrentDirectory, ImportTestsDirectoryPath, @"Import\NestedTargets.targets"), false),
                new BuildInput(Path.Combine(Environment.CurrentDirectory, ImportTestsDirectoryPath, @"Import\NestedTargets2.targets"), false)
            };

            BuildOutputDirectory[] expectedOutputs = null;

            var predictor = new ProjectFileAndImportedFiles();
            ParseAndVerifyProject(NestedImportsProjectFileName, predictor, expectedInputs, expectedOutputs);
        }
    }
}
