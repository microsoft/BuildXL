// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Scheduler.Filter
{
    /// <summary>
    /// How spec should be matched against the path specified in the filter
    /// </summary>
    public enum MatchMode
    {
        /// <summary>
        /// Matches a file with the exact file path
        /// </summary>
        FilePath,

        /// <summary>
        /// Matches files within the given directory but not subdirectories
        /// </summary>
        WithinDirectory,

        /// <summary>
        /// Matches files in the directory and all subdirectories of the given path
        /// </summary>
        WithinDirectoryAndSubdirectories,

        /// <summary>
        /// A path filter with a prefix wildcard. ex: '*.txt'
        /// </summary>
        PathPrefixWildcard,

        /// <summary>
        /// A path with a suffix wildcard. ex: 'Product.Component.*'
        /// </summary>
        PathSuffixWildcard,

        /// <summary>
        /// A path with both a prefix and suffix wildcard. ex: '*\out\bin\release\*'
        /// </summary>
        PathPrefixAndSuffixWildcard,
    }
}
