// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;

namespace Microsoft.Build.Prediction
{
    /// <summary>
    /// Executes a set of <see cref="IProjectStaticPredictor"/> instances against
    /// a <see cref="Microsoft.Build.Evaluation.Project"/> instance, aggregating
    /// the result.
    /// </summary>
    public sealed class ProjectStaticPredictionExecutor
    {
        private readonly string _repositoryRootDirectory;
        private readonly PredictorAndName[] _predictors;

        /// <summary>Initializes a new instance of the <see cref="ProjectStaticPredictionExecutor"/> class.</summary>
        /// <param name="repositoryRootDirectory">
        /// The filesystem directory containing the source code of the repository. For Git submodules
        /// this is typically the directory of the outermost containing repository.
        /// This is used for normalization of predicted paths.
        /// </param>
        /// <param name="predictors">The set of <see cref="IProjectStaticPredictor"/> instances to use for prediction.</param>
        public ProjectStaticPredictionExecutor(
            string repositoryRootDirectory,
            IEnumerable<IProjectStaticPredictor> predictors)
        {
            _repositoryRootDirectory = repositoryRootDirectory.ThrowIfNullOrEmpty(nameof(repositoryRootDirectory));
            _predictors = predictors
                .ThrowIfNull(nameof(predictors))
                .Select(p => new PredictorAndName(p))
                .ToArray();  // Array = faster parallel performance.
        }

        /// <summary>
        /// Executes all predictors in parallel against the provided Project and aggregates
        /// the results into one set of predictions. All paths in the final predictions are
        /// fully qualified paths, not relative to the directory containing the Project or
        /// to the repository root directory, since inputs and outputs could lie outside of
        /// that directory.
        /// </summary>
        /// <returns>An object describing all predicted inputs and outputs.</returns>
        public StaticPredictions PredictInputsAndOutputs(Project project)
        {
            project.ThrowIfNull(nameof(project));

            // Squash the Project with its full XML contents and tracking down to
            // a more memory-efficient format that can be used to evaluate conditions.
            // TODO: Static Graph needs to provide both, not just ProjectInstance, when we integrate.
            ProjectInstance projectInstance = project.CreateProjectInstance(ProjectInstanceSettings.ImmutableWithFastItemLookup);

            // Perf: Compared ConcurrentQueue vs. static array of results,
            // queue is 25% slower when all predictors return empty results,
            // ~25% faster as predictors return more and more false/null results,
            // with the breakeven point in the 10-15% null range.
            // ConcurrentBag 10X worse than either of the above, ConcurrentStack about the same.
            // Keeping queue implementation since many predictors return false.
            var results = new ConcurrentQueue<StaticPredictions>();
            Parallel.For(
                0,
                _predictors.Length,
                i =>
                {
                    bool success = _predictors[i].Predictor.TryPredictInputsAndOutputs(
                        project,
                        projectInstance,
                        _repositoryRootDirectory,
                        out StaticPredictions result);

                    // Tag each prediction with its source.
                    // Check for null even on success as a bad predictor could do that.
                    if (success && result != null)
                    {
                        foreach (BuildInput item in result.BuildInputs)
                        {
                            item.AddPredictedBy(_predictors[i].TypeName);
                        }

                        foreach (BuildOutputDirectory item in result.BuildOutputDirectories)
                        {
                            item.AddPredictedBy(_predictors[i].TypeName);
                        }

                        results.Enqueue(result);
                    }
                });

            var inputsByPath = new Dictionary<string, BuildInput>(PathComparer.Instance);
            var outputDirectoriesByPath = new Dictionary<string, BuildOutputDirectory>(PathComparer.Instance);

            foreach (StaticPredictions predictions in results)
            {
                // TODO: Determine policy when dup inputs vary by IsDirectory.
                foreach (BuildInput input in predictions.BuildInputs)
                {
                    if (inputsByPath.TryGetValue(input.Path, out BuildInput existingInput))
                    {
                        existingInput.AddPredictedBy(input.PredictedBy);
                    }
                    else
                    {
                        inputsByPath[input.Path] = input;
                    }
                }

                foreach (BuildOutputDirectory outputDir in predictions.BuildOutputDirectories)
                {
                    if (outputDirectoriesByPath.TryGetValue(outputDir.Path, out BuildOutputDirectory existingOutputDir))
                    {
                        existingOutputDir.AddPredictedBy(outputDir.PredictedBy);
                    }
                    else
                    {
                        outputDirectoriesByPath[outputDir.Path] = outputDir;
                    }
                }
            }

            return new StaticPredictions(inputsByPath.Values, outputDirectoriesByPath.Values);
        }

        private readonly struct PredictorAndName
        {
            public readonly IProjectStaticPredictor Predictor;

            /// <summary>
            /// Cached type name - we expect predictor instances to be reused many times in
            /// an overall parsing session, avoid doing the reflection over and over in
            /// the prediction methods.
            /// </summary>
            public readonly string TypeName;

            public PredictorAndName(IProjectStaticPredictor predictor)
            {
                Predictor = predictor;
                TypeName = predictor.GetType().Name;
            }
        }
    }
}
