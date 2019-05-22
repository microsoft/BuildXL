// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;

namespace Microsoft.Build.Prediction.StandardPredictors
{
    /// <summary>
    /// Scrapes the $(IntermediateOutputPath) if found.
    /// </summary>
    internal class IntermediateOutputPathIsOutputDir : IProjectStaticPredictor
    {
        internal const string IntermediateOutputPathMacro = "IntermediateOutputPath";

        public bool TryPredictInputsAndOutputs(
            Project project,
            ProjectInstance projectInstance,
            string repositoryRootDirectory,
            out StaticPredictions predictions)
        {
            string intermediateOutputPath = project.GetPropertyValue(IntermediateOutputPathMacro);

            if (string.IsNullOrWhiteSpace(intermediateOutputPath))
            {
                // It is not defined, so we don't return a result.
                predictions = null;
                return false;
            }

            // If the path is relative, it is interpreted as relative to the project directory path
            string predictedOutputDirectory;
            if (!Path.IsPathRooted(intermediateOutputPath))
            {
                predictedOutputDirectory = Path.Combine(project.DirectoryPath, intermediateOutputPath);
            }
            else
            {
                predictedOutputDirectory = intermediateOutputPath;
            }

            predictions = new StaticPredictions(
                buildInputs: null,
                buildOutputDirectories: new[] { new BuildOutputDirectory(Path.GetFullPath(predictedOutputDirectory)) });
            return true;
        }
    }
}
