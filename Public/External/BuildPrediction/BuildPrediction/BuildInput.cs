// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace Microsoft.Build.Prediction
{
    /// <summary>
    /// Data class for specifying a predicted build input, either a file or a directory.
    /// </summary>
    public class BuildInput : BuildItem
    {
        internal static readonly IEqualityComparer<BuildInput> ComparerInstance = new BuildInputComparer();

        /// <summary>Initializes a new instance of the <see cref="BuildInput"/> class.</summary>
        /// <param name="path">
        /// Provides a rooted path to a predicted build input.
        /// </param>
        /// <param name="isDirectory">When true, the predicted path is a directory instead of a file.</param>
        public BuildInput(string path, bool isDirectory)
            : base(path)
        {
            IsDirectory = isDirectory;
        }

        internal BuildInput(string path, bool isDirectory, params string[] predictedBys)
            : base(path, predictedBys)
        {
            IsDirectory = isDirectory;
        }

        /// <summary>
        /// Gets a value indicating whether the predicted path is a directory instead of a file.
        /// </summary>
        public bool IsDirectory { get; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"BuildInput: {Path} IsDir={IsDirectory} PredictedBy={string.Join(",", PredictedBy)}";
        }
    }
}
