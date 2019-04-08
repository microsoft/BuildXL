// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace Microsoft.Build.Prediction
{
    /// <summary>
    /// Data class for specifying a predicted output directory from an MSBuild <see cref="Microsoft.Build.Evaluation.Project"/>.
    /// </summary>
    public class BuildOutputDirectory : BuildItem
    {
        internal static readonly IEqualityComparer<BuildOutputDirectory> ComparerInstance = new BuildOutputDirectoryComparer();

        /// <summary>Initializes a new instance of the <see cref="BuildOutputDirectory"/> class.</summary>
        /// <param name="path">Provides a rooted path to the output directory.</param>
        public BuildOutputDirectory(string path)
            : base(path)
        {
        }

        internal BuildOutputDirectory(string path, params string[] predictedBys)
            : base(path, predictedBys)
        {
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"BuildOutputDirectory: {Path} PredictedBy={string.Join(",", PredictedBy)}";
        }
    }
}
