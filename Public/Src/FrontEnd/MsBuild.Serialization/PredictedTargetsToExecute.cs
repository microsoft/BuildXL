// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using Newtonsoft.Json;

namespace BuildXL.FrontEnd.MsBuild.Serialization
{
    /// <summary>
    /// List of targets to execute that was predicted based on projects following the project reference
    /// protocol (https://github.com/Microsoft/msbuild/blob/master/documentation/specs/static-graph.md#inferring-which-targets-to-run-for-a-project-within-the-graph)
    /// </summary>
    public sealed class PredictedTargetsToExecute
    {
        /// <summary>
        /// When true, this is an indication that some project is not implementing the target protocol and referencing a project that contains these targets.
        /// </summary>
        /// <remarks>
        /// When such a case is allowed, the heuristic is to append default targets to the statically predicted target list
        /// </remarks>
        public bool IsDefaultTargetsAppended { get; }
        
        /// <nodoc/>
        public IReadOnlyCollection<string> Targets { get; }

        /// <summary>
        /// If <see cref="IsDefaultTargetsAppended"/>, the collection of default targets already appended to <see cref="Targets"/>. Null otherwise.
        /// </summary>
        /// <remarks>
        /// Intended for error reporting purposes
        /// </remarks>
        public IReadOnlyCollection<string> AppendedDefaultTargets { get; }

        [JsonConstructor]
        private PredictedTargetsToExecute(bool isDefaultTargetsAppended, IReadOnlyCollection<string> targets, IReadOnlyCollection<string> appendedDefaultTargets)
        {
            IsDefaultTargetsAppended = isDefaultTargetsAppended;
            Targets = targets;
            AppendedDefaultTargets = appendedDefaultTargets;
        }

        /// <nodoc/>
        public static PredictedTargetsToExecute Create(IReadOnlyCollection<string> targets)
        {
            Contract.Requires(targets != null);
            return new PredictedTargetsToExecute(isDefaultTargetsAppended: false, targets: targets, appendedDefaultTargets: null);
        }

        /// <summary>
        /// Returns a new instance of this class with the provided targets appended to the existing targets of this
        /// </summary>
        public PredictedTargetsToExecute WithDefaultTargetsAppended(IReadOnlyCollection<string> defaultTargets)
        {
            Contract.Requires(defaultTargets != null);
            Contract.Requires(!IsDefaultTargetsAppended);

            // If default targets are empty, then the operation is idempotent
            if (defaultTargets.Count == 0)
            {
                return this;
            }

            return new PredictedTargetsToExecute(isDefaultTargetsAppended: true, Targets.Union(defaultTargets).ToList(), defaultTargets);
        }
    }
}
