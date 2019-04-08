// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;

namespace Microsoft.Build.Prediction.StandardPredictors
{
    /// <summary>
    /// Scrapes the $(OutDir) or, if not found, $(OutputPath) as an output directory.
    /// </summary>
    internal class OutDirOrOutputPathIsOutputDir : IProjectStaticPredictor
    {
        internal const string OutDirMacro = "OutDir";
        internal const string OutputPathMacro = "OutputPath";

        public bool TryPredictInputsAndOutputs(
            Project project,
            ProjectInstance projectInstance,
            string repositoryRootDirectory,
            out StaticPredictions predictions)
        {
            string outDir = project.GetPropertyValue(OutDirMacro);
            string outputPath = project.GetPropertyValue(OutputPathMacro);

            // For an MSBuild project, the output goes to $(OutDir) by default. Usually $(OutDir)
            // equals $(OutputPath). Many targets expect OutputPath/OutDir to be defined and
            // MsBuild.exe reports an error if these macros are undefined.
            string finalOutputPath;
            if (!string.IsNullOrWhiteSpace(outDir))
            {
                finalOutputPath = outDir;
            }
            else if (!string.IsNullOrWhiteSpace(outputPath))
            {
                // Some projects use custom code with $(OutputPath) set instead of following the common .targets pattern.
                // Fall back to $(OutputPath) first when $(OutDir) is not set.
                finalOutputPath = outputPath;
            }
            else
            {
                // Neither is defined, we don't return a result.
                predictions = null;
                return false;
            }

            // If the path is relative, it is interpreted as relative to the project directory path
            string predictedOutputDirectory;
            if (!Path.IsPathRooted(finalOutputPath))
            {
                predictedOutputDirectory = Path.Combine(project.DirectoryPath, finalOutputPath);
            }
            else
            {
                predictedOutputDirectory = finalOutputPath;
            }

            predictions = new StaticPredictions(
                buildInputs: null,
                buildOutputDirectories: new[] { new BuildOutputDirectory(Path.GetFullPath(predictedOutputDirectory)) });
            return true;
        }
    }
}
