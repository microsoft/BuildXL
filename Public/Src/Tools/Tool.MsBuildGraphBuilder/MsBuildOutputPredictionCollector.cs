// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using Microsoft.Build.Prediction;
using Microsoft.Build.Execution;

namespace MsBuildGraphBuilderTool
{
    /// <summary>
    /// Collects output predictions from the MSBuild prediction library.
    /// </summary>
    /// <remarks>
    /// This validates the predicted outputs, in case the predictor is not working properly
    /// </remarks>
    public sealed class MsBuildOutputPredictionCollector : IProjectPredictionCollector
    {
        private readonly ConcurrentQueue<(string predictorName, string failure)> m_predictionFailures;

        private readonly ICollection<string> m_outputFolderPredictions;

        /// <summary>
        /// Initializes a new instance of the <see cref="MsBuildOutputPredictionCollector"/> class.
        /// </summary>
        /// <remarks>
        /// The provided collections will be added to by this collector as predictions (or prediction errors) happen.
        /// </remarks>
        /// <param name="outputFolderPredictions">A collection of output folder predictions the collector should add to as needed.</param>
        /// <param name="predictionFailures">A collection of prediction failures the collector should add to as needed.</param>
        public MsBuildOutputPredictionCollector(
            ICollection<string> outputFolderPredictions,
            ConcurrentQueue<(string predictorName, string failure)> predictionFailures)
        {
            Contract.Assert(outputFolderPredictions != null);
            Contract.Assert(predictionFailures != null);

            m_outputFolderPredictions = outputFolderPredictions;
            m_predictionFailures = predictionFailures;
        }

        /// <inheritdoc/>
        public void AddInputFile(string path, ProjectInstance projectInstance, string predictorName)
        {
            // We don't collect inputs
        }

        /// <inheritdoc/>
        public void AddInputDirectory(string path, ProjectInstance projectInstance, string predictorName)
        {
            // We don't collect input directories
        }

        /// <inheritdoc/>
        public void AddOutputFile(string path, ProjectInstance projectInstance, string predictorName)
        {
            if (!TryValidatePrediction(path, projectInstance, predictorName, out string absolutePath))
            {
                return;
            }

            string folder = Path.GetDirectoryName(absolutePath);
            m_outputFolderPredictions.Add(folder);
        }

        /// <inheritdoc/>
        public void AddOutputDirectory(string path, ProjectInstance projectInstance, string predictorName)
        {
            if (!TryValidatePrediction(path, projectInstance, predictorName, out string absolutePath))
            {
                return;
            }

            m_outputFolderPredictions.Add(absolutePath);
        }

        private bool TryValidatePrediction(string path, ProjectInstance projectInstance, string predictorName, out string absolutePath)
        {
            try
            {
                absolutePath = Path.IsPathRooted(path)
                    ? path
                    : Path.GetFullPath(Path.Combine(projectInstance.Directory, path));
                return true;
            }
            catch (ArgumentException e)
            {
                absolutePath = null;
                m_predictionFailures.Enqueue((predictorName, $"The predicted path '{path}' is malformed: {e.Message}"));
                return false;
            }
        }
    }
}