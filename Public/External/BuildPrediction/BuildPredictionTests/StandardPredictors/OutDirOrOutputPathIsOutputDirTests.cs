// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Prediction.StandardPredictors;
using Xunit;

namespace Microsoft.Build.Prediction.Tests.StandardPredictors
{
    public class OutDirOrOutputPathIsOutputDirTests
    {
        static OutDirOrOutputPathIsOutputDirTests()
        {
            MsBuildEnvironment.Setup(TestHelpers.GetAssemblyLocation());
        }

        [Fact]
        public void OutDirFoundAsOutputDir()
        {
            const string outDir = @"C:\repo\bin\x64";
            Project project = CreateTestProject(outDir, null);
            ProjectInstance projectInstance = project.CreateProjectInstance(ProjectInstanceSettings.ImmutableWithFastItemLookup);
            var predictor = new OutDirOrOutputPathIsOutputDir();
            bool hasPredictions = predictor.TryPredictInputsAndOutputs(project, projectInstance, @"C:\repo", out StaticPredictions predictions);
            Assert.True(hasPredictions);
            predictions.AssertPredictions(null, new[] { new BuildOutputDirectory(outDir) });
        }

        [Fact]
        public void OutputPathUsedAsFallback()
        {
            const string outputPath = @"C:\repo\OutputPath";
            Project project = CreateTestProject(null, outputPath);
            ProjectInstance projectInstance = project.CreateProjectInstance(ProjectInstanceSettings.ImmutableWithFastItemLookup);
            var predictor = new OutDirOrOutputPathIsOutputDir();
            bool hasPredictions = predictor.TryPredictInputsAndOutputs(project, projectInstance, @"C:\repo", out StaticPredictions predictions);
            Assert.True(hasPredictions);
            predictions.AssertPredictions(null, new[] { new BuildOutputDirectory(outputPath) });
        }

        [Fact]
        public void NoOutputsReportedIfNoOutDirOrOutputPath()
        {
            Project project = CreateTestProject(null, null);
            ProjectInstance projectInstance = project.CreateProjectInstance(ProjectInstanceSettings.ImmutableWithFastItemLookup);
            var predictor = new OutDirOrOutputPathIsOutputDir();
            bool hasPredictions = predictor.TryPredictInputsAndOutputs(project, projectInstance, @"C:\repo", out _);
            Assert.False(hasPredictions, "Predictor should have fallen back to returning no predictions if OutDir and OutputPath are not defined in project");
        }

        private static Project CreateTestProject(string outDir, string outputPath)
        {
            ProjectRootElement projectRootElement = ProjectRootElement.Create();
            if (outDir != null)
            {
                projectRootElement.AddProperty(OutDirOrOutputPathIsOutputDir.OutDirMacro, outDir);
            }

            if (outputPath != null)
            {
                projectRootElement.AddProperty(OutDirOrOutputPathIsOutputDir.OutputPathMacro, outputPath);
            }

            return TestHelpers.CreateProjectFromRootElement(projectRootElement);
        }
    }
}
