// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Prediction;

namespace MsBuildGraphBuilderTool
{
    /// <summary>
    /// Collects predictions from the MsBuild prediction library.
    /// </summary>
    /// <remarks>
    /// This validates the predicted inputs and outputs, in case the predictor is not working properly
    /// </remarks>
    public sealed class MsBuildPredictionCollector : IProjectPredictionCollector
    {
        private readonly ConcurrentQueue<(string predictorName, string failure)> m_predictionFailures;

        private readonly ICollection<string> m_inputFilePredictions;

        private readonly ICollection<string> m_outputFolderPredictions;

        public MsBuildPredictionCollector(
            ICollection<string> inputFilePredictions,
            ICollection<string> outputFolderPredictions,
            ConcurrentQueue<(string predictorName, string failure)> predictionFailures)
        {
            m_inputFilePredictions = inputFilePredictions;
            m_outputFolderPredictions = outputFolderPredictions;
            m_predictionFailures = predictionFailures;
        }

        public void AddInputFile(string path, string projectDirectory, string predictorName)
        {
            if (!TryValidatePrediction(path, projectDirectory, predictorName, out string absolutePath))
            {
                return;
            }

            m_inputFilePredictions.Add(absolutePath);
        }

        public void AddInputDirectory(string path, string projectDirectory, string predictorName)
        {
            if (!TryValidatePrediction(path, projectDirectory, predictorName, out string absolutePath))
            {
                return;
            }

            if (Directory.Exists(absolutePath))
            {
                foreach (string file in Directory.EnumerateFiles(absolutePath))
                {
                    m_inputFilePredictions.Add(file);
                }
            }
            // TODO: Can we do anything to flag that the input prediction is not going to be used?
        }

        public void AddOutputFile(string path, string projectDirectory, string predictorName)
        {
            if (!TryValidatePrediction(path, projectDirectory, predictorName, out string absolutePath))
            {
                return;
            }

            string folder = Path.GetDirectoryName(absolutePath);
            m_outputFolderPredictions.Add(folder);
        }

        public void AddOutputDirectory(string path, string projectDirectory, string predictorName)
        {
            if (!TryValidatePrediction(path, projectDirectory, predictorName, out string absolutePath))
            {
                return;
            }

            m_outputFolderPredictions.Add(absolutePath);
        }

        private bool TryValidatePrediction(string path, string projectDirectory, string predictorName, out string absolutePath)
        {
            try
            {
                absolutePath = Path.IsPathRooted(path)
                    ? path
                    : Path.GetFullPath(Path.Combine(projectDirectory, path));
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