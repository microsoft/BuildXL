// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Build.Prediction
{
    /// <summary>
    /// Base class for <see cref="BuildInput"/> and <see cref="BuildOutputDirectory"/>.
    /// </summary>
    public abstract class BuildItem
    {
        private readonly HashSet<string> _predictedBy = new HashSet<string>(StringComparer.Ordinal);

        // For unit testing
        internal BuildItem(string path, params string[] predictedBys)
            : this(path)
        {
            AddPredictedBy(predictedBys);
        }

        /// <summary>Initializes a new instance of the <see cref="BuildItem"/> class.</summary>
        /// <param name="path">
        /// Provides a rooted path to a predicted build input.
        /// </param>
        protected BuildItem(string path)
        {
            Path = path.ThrowIfNullOrEmpty(nameof(path));
        }

        /// <summary>
        /// Gets a relative or (on Windows) rooted path to a predicted build item.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Gets the class name of each contributor to this prediction, for debugging purposes.
        /// These values are set in internally by <see cref="ProjectStaticPredictionExecutor"/>.
        /// </summary>
        public IReadOnlyCollection<string> PredictedBy => _predictedBy;

        /// <summary>
        /// Used for combining BuildInputs.
        /// </summary>
        internal void AddPredictedBy(IEnumerable<string> predictedBys)
        {
            foreach (string p in predictedBys)
            {
                _predictedBy.Add(p);
            }
        }

        internal void AddPredictedBy(string predictedBy)
        {
            _predictedBy.Add(predictedBy);
        }
    }
}
