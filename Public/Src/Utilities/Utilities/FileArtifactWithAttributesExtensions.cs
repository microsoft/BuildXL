// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities
{
    /// <summary>
    /// Set of extensions methods for <see cref="FileArtifactWithAttributes"/> type.
    /// </summary>
    public static class FileArtifactWithAttributesExtensions
    {
        /// <summary>
        /// Returns true if the artifact should be present before and/or after pip execution.
        /// </summary>
        public static bool MustExist(in this FileArtifactWithAttributes @this)
        {
            return @this.IsOutputFile && @this.IsRequiredOutputFile;
        }

        /// <summary>
        /// Returns true if the file artifact can be referenced by another pip or stored in the cache.
        /// </summary>
        public static bool CanBeReferencedOrCached(in this FileArtifactWithAttributes @this)
        {
            // Required or optional output files can be referenced. Temporary files can't.
            return @this.IsOutputFile && !@this.IsTemporaryOutputFile;
        }

        /// <summary>
        /// Returns true if the file artifact should be deleted before running the pip that produces it.
        /// </summary>
        public static bool DeleteBeforeRun(in this FileArtifactWithAttributes @this)
        {
            return @this.IsOutputFile;
        }
    }
}
