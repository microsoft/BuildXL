// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using Newtonsoft.Json;

namespace BuildXL.FrontEnd.MsBuild.Serialization
{
    /// <summary>
    /// List of targets to execute that was predicted based on projects following the project reference
    /// protocol (https://github.com/Microsoft/msbuild/blob/master/documentation/specs/static-graph.md#inferring-which-targets-to-run-for-a-project-within-the-graph)
    /// </summary>
    /// <remarks>
    /// The prediction may not be available
    /// </remarks>
    public sealed class PredictedTargetsToExecute
    {
        /// <nodoc/>
        public bool IsPredictionAvailable { get; }
        
        /// <nodoc/>
        public IReadOnlyCollection<string> Targets { get; }

        /// <summary>
        /// Targets are successfully predicted, and there are none
        /// </summary>
        public bool TargetsAreKnownToBeEmpty => IsPredictionAvailable && Targets.Count == 0;

        [JsonConstructor]
        private PredictedTargetsToExecute(bool isPredictionAvailable, IReadOnlyCollection<string> targets)
        {
            IsPredictionAvailable = isPredictionAvailable;
            Targets = targets;
        }

        /// <nodoc/>
        public static PredictedTargetsToExecute PredictionNotAvailable { get; } = new PredictedTargetsToExecute(isPredictionAvailable: false, targets: null);

        /// <nodoc/>
        public static PredictedTargetsToExecute CreatePredictedTargetsToExecute(IReadOnlyCollection<string> targets)
        {
            Contract.Requires(targets != null);
            return new PredictedTargetsToExecute(isPredictionAvailable: true, targets: targets);
        }
    }
}
