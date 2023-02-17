// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Core;

namespace BuildXL.Pips.Graph
{
    /// <summary>
    /// Class for creating output directory artifacts.
    /// </summary>
    public static class OutputDirectory
    {
        /// <summary>
        /// Creates an output directory from a path.
        /// </summary>
        /// <param name="path">Root path for the directory.</param>
        /// <returns>Directory artifact whose root is <paramref name="path" />.</returns>
        public static DirectoryArtifact Create(AbsolutePath path)
        {
            // Output directory has partial seal id 0.
            return DirectoryArtifact.CreateWithZeroPartialSealId(path);
        }

        /// <summary>
        /// Checks if a directory artifact is an output directory.
        /// </summary>
        /// <param name="directory">Directory to be checked.</param>
        /// <returns>True if <paramref name="directory" /> is an output directory.</returns>
        public static bool IsOutputDirectory(this DirectoryArtifact directory)
        {
            return (directory.IsValid && directory.PartialSealId == 0) || 
                   (directory.IsValid && directory.IsSharedOpaque && directory.PartialSealId > 0);
        }
    }
}
