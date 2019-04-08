// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;

namespace Microsoft.Build.Prediction.StandardPredictors.CopyTask
{
    /// <summary>
    /// Parses Copy tasks from Targets in the provided Project to predict inputs
    /// and outputs.
    /// </summary>
    /// <remarks>
    /// This predictor assumes that the Build target is the primary for MSBuild evaluation,
    /// and follows the Targets activated by that target, along with all custom Targets
    /// present in the current project file.
    /// </remarks>
    public class CopyTaskPredictor : IProjectStaticPredictor
    {
        private const string CopyTaskName = "Copy";
        private const string CopyTaskSourceFiles = "SourceFiles";
        private const string CopyTaskDestinationFiles = "DestinationFiles";
        private const string CopyTaskDestinationFolder = "DestinationFolder";

        /// <inheritdoc />
        public bool TryPredictInputsAndOutputs(
            Project project,
            ProjectInstance projectInstance,
            string repositoryRootDirectory,
            out StaticPredictions predictions)
        {
            // Determine the active Targets in this Project.
            var activeTargets = new Dictionary<string, ProjectTargetInstance>(StringComparer.OrdinalIgnoreCase);
            
            // Start with the default Build target and all of its parent targets, the closure of its dependencies.
            project.AddToActiveTargets(MsBuildHelpers.BuildTargetAsCollection, activeTargets);

            // Aside from InitialTargets and DefaultTargets, for completeness of inputs/outputs detection,
            // include custom targets defined directly in this Project.
            // Note that this misses targets defined in any custom targets files.
            foreach (ProjectTargetInstance target in projectInstance.Targets.Values
                .Where(t => string.Equals(t.Location.File, project.ProjectFileLocation.File, PathComparer.Comparison)))
            {
                project.AddToActiveTargets(new[] { target.Name }, activeTargets);
            }

            project.AddBeforeAndAfterTargets(activeTargets);

            // Then parse copy tasks for these targets.
            var buildInputs = new HashSet<BuildInput>(BuildInput.ComparerInstance);
            var buildOutputDirectories = new HashSet<string>(PathComparer.Instance);
            foreach (KeyValuePair<string, ProjectTargetInstance> target in activeTargets)
            {
                ParseCopyTask(target.Value, projectInstance, buildInputs, buildOutputDirectories);
            }

            if (buildInputs.Count > 0)
            {
                predictions = new StaticPredictions(
                    buildInputs,
                    buildOutputDirectories.Select(o => new BuildOutputDirectory(o)).ToList());
                return true;
            }

            predictions = null;
            return false;
        }

        /// <summary>
        /// Parses the input and output files for copy tasks of given target.
        /// </summary>
        private static void ParseCopyTask(
            ProjectTargetInstance target,
            ProjectInstance projectInstance,
            HashSet<BuildInput> buildInputs,
            HashSet<string> buildOutputDirectories)
        {
            // Get all Copy tasks from targets.
            List<ProjectTaskInstance> tasks = target.Tasks
                .Where(task => string.Equals(task.Name, CopyTaskName, StringComparison.Ordinal))
                .ToList();

            if (tasks.Any() && projectInstance.EvaluateConditionCarefully(target.Condition))
            {
                foreach (ProjectTaskInstance task in tasks)
                {
                    if (projectInstance.EvaluateConditionCarefully(task.Condition))
                    {
                        var inputs = new FileExpressionList(
                            task.Parameters[CopyTaskSourceFiles],
                            projectInstance,
                            task);
                        if (inputs.Expressions.Count == 0)
                        {
                            continue;
                        }

                        buildInputs.UnionWith(inputs.DedupedFiles.
                            Select(file => new BuildInput(Path.Combine(projectInstance.Directory, file), false)));

                        bool hasDestinationFolder = task.Parameters.TryGetValue(
                            CopyTaskDestinationFolder,
                            out string destinationFolder);
                        bool hasDestinationFiles = task.Parameters.TryGetValue(
                            CopyTaskDestinationFiles,
                            out string destinationFiles);

                        if (hasDestinationFiles || hasDestinationFolder)
                        {
                            // Having both is an MSBuild violation, which it will complain about.
                            if (hasDestinationFolder && hasDestinationFiles)
                            {
                                continue;
                            }

                            string destination = destinationFolder ?? destinationFiles;

                            var outputs = new FileExpressionList(destination, projectInstance, task);

                            // When using batch tokens, the user should specify exactly one total token, and it must appear in both the input and output.
                            // Doing otherwise should be a BuildCop error. If not using batch tokens, then any number of other tokens is fine.
                            if ((outputs.NumBatchExpressions == 1 && outputs.Expressions.Count == 1 &&
                                 inputs.NumBatchExpressions == 1 && inputs.Expressions.Count == 1) ||
                                (outputs.NumBatchExpressions == 0 && inputs.NumBatchExpressions == 0))
                            {
                                ProcessOutputs(projectInstance.FullPath, inputs, outputs, hasDestinationFolder, buildOutputDirectories);
                            }
                            else
                            {
                                // Ignore case we cannot handle.
                            }
                        }
                        else
                        {
                            // Ignore malformed case.
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Validates that a task's outputs are sane. If so, predicts output directories.
        /// </summary>
        /// <param name="projectFullPath">The absolute path to the project instance. Can be null if the project was not loaded
        /// from this</param>
        /// <param name="inputs">The inputs specified in SourceFiles on a copy task.</param>
        /// <param name="outputs">
        /// The outputs specified in the DestinationFolder or DestinationFiles attribute on a copy task.
        /// </param>
        /// <param name="copyTaskSpecifiesDestinationFolder">True if the user has specified DestinationFolder.</param>
        /// <param name="buildOutputDirectories">Collection to fill with output folder predictions.</param>
        private static void ProcessOutputs(
            string projectFullPath,
            FileExpressionList inputs,
            FileExpressionList outputs,
            bool copyTaskSpecifiesDestinationFolder,
            HashSet<string> buildOutputDirectories)
        {
            for (int i = 0; i < inputs.DedupedFiles.Count; i++)
            {
                string predictedOutputDirectory;

                // If the user specified a destination folder, they could have specified an expression that evaluates to
                // either exactly one or N folders. We need to handle each case.
                if (copyTaskSpecifiesDestinationFolder)
                {
                    if (outputs.DedupedFiles.Count == 0)
                    {
                        // Output files couldn't be parsed, bail out.
                        break;
                    }

                    // If output directories isn't 1 or N, bail out.
                    if (inputs.DedupedFiles.Count != outputs.DedupedFiles.Count && outputs.DedupedFiles.Count > 1)
                    {
                        break;
                    }

                    predictedOutputDirectory = outputs.DedupedFiles.Count == 1 ? outputs.DedupedFiles[0] : outputs.DedupedFiles[i];
                }
                else
                {
                    if (i >= outputs.DedupedFiles.Count)
                    {
                        break;
                    }

                    // The output list is a set of files. Predict their directories.
                    predictedOutputDirectory = Path.GetDirectoryName(outputs.DedupedFiles[i]);
                }

                // If the predicted directory is not absolute, let's try to make it absolute using the project full path
                if (!TryMakePathAbsoluteIfNeeded(predictedOutputDirectory, projectFullPath, out string absolutePathPrediction))
                {
                    // The project full path is not available, so just ignore this prediction
                    continue;
                }

                buildOutputDirectories.Add(absolutePathPrediction);
            }
        }

        private static bool TryMakePathAbsoluteIfNeeded(string path, string projectFullPath, out string absolutePath)
        {
            if (!Path.IsPathRooted(path))
            {
                if (string.IsNullOrEmpty(projectFullPath))
                {
                    absolutePath = string.Empty;
                    return false;
                }

                absolutePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectFullPath), path));
                return true;
            }

            absolutePath = path;
            return true;
        }
    }
}
