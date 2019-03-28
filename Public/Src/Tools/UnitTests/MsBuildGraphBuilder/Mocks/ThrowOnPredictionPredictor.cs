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
    public class ThrowOnPredictionPredictor : IProjectStaticPredictor
    {
        /// <inheritdoc/>
        public bool TryPredictInputsAndOutputs(Project project, ProjectInstance projectInstance, string repositoryRootDirectory, out StaticPredictions predictions)
        {
            throw new InvalidOperationException();
        }
    }
}
