// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Prediction.StandardPredictors;
using Xunit;

namespace Microsoft.Build.Prediction.Tests.StandardPredictors
{
    public class IntermediateOutputPathIsOutputDirTests
    {
        static IntermediateOutputPathIsOutputDirTests()
        {
            MsBuildEnvironment.Setup(TestHelpers.GetAssemblyLocation());
        }

        [Fact]
        public void IntermediateOutputPathFoundAsOutputDir()
        {
            const string IntermediateOutputPath = @"C:\repo\bin\x64";
            Project project = CreateTestProject(IntermediateOutputPath);
            ProjectInstance projectInstance = project.CreateProjectInstance(ProjectInstanceSettings.ImmutableWithFastItemLookup);
            var predictor = new IntermediateOutputPathIsOutputDir();
            bool hasPredictions = predictor.TryPredictInputsAndOutputs(project, projectInstance, @"C:\repo", out StaticPredictions predictions);
            Assert.True(hasPredictions);
            predictions.AssertPredictions(null, new[] { new BuildOutputDirectory(IntermediateOutputPath) });
        }

        [Fact]
        public void RelativeIntermediateOutputPathFoundAsOutputDir()
        {
            const string IntermediateOutputPath = @"bin\x64";
            Project project = CreateTestProject(IntermediateOutputPath);
            ProjectInstance projectInstance = project.CreateProjectInstance(ProjectInstanceSettings.ImmutableWithFastItemLookup);
            var predictor = new IntermediateOutputPathIsOutputDir();
            bool hasPredictions = predictor.TryPredictInputsAndOutputs(project, projectInstance, @"C:\repo", out StaticPredictions predictions);
            Assert.True(hasPredictions);
            predictions.AssertPredictions(null, new[] { new BuildOutputDirectory(Path.Combine(Directory.GetCurrentDirectory(), IntermediateOutputPath)) });
        }

        [Fact]
        public void NoOutputsReportedIfNoIntermediateOutputPath()
        {
            Project project = CreateTestProject(null);
            ProjectInstance projectInstance = project.CreateProjectInstance(ProjectInstanceSettings.ImmutableWithFastItemLookup);
            var predictor = new IntermediateOutputPathIsOutputDir();
            bool hasPredictions = predictor.TryPredictInputsAndOutputs(project, projectInstance, @"C:\repo", out _);
            Assert.False(hasPredictions, "Predictor should have fallen back to returning no predictions if IntermediateOutputDir is not defined in project");
        }

        private static Project CreateTestProject(string intermediateOutDir)
        {
            ProjectRootElement projectRootElement = ProjectRootElement.Create();
            if (intermediateOutDir != null)
            {
                projectRootElement.AddProperty(IntermediateOutputPathIsOutputDir.IntermediateOutputPathMacro, intermediateOutDir);
            }

            return TestHelpers.CreateProjectFromRootElement(projectRootElement);
        }
    }
}
