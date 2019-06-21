// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Prediction;

namespace Test.Tool.ProjectGraphBuilder.Mocks
{
    /// <summary>
    /// A predictor that throws on prediction
    /// </summary>
    public class ThrowOnPredictionPredictor : IProjectPredictor
    {
        /// <inheritdoc/>
        public void PredictInputsAndOutputs(Project project, ProjectInstance projectInstance, ProjectPredictionReporter predictionReporter)
        {
            throw new InvalidOperationException();
        }
    }
}
