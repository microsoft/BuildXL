// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Utilities;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace BuildXL.Scheduler.Filter
{
    /// <summary>
    /// Base for filters that match file paths
    /// </summary>
    public abstract class PathBasedFilter : PipFilter
    {
        private readonly AbsolutePath m_path;
        private readonly MatchMode m_matchMode;
        private readonly string m_pathWildcardUpper;
        protected readonly bool PathFromMount;

        /// <summary>
        /// Constructor
        /// </summary>
        protected PathBasedFilter(AbsolutePath path, string pathWildcard, MatchMode matchMode, bool pathFromMount)
        {
            Contract.Requires(((matchMode == MatchMode.PathPrefixWildcard) || (matchMode == MatchMode.PathSuffixWildcard) || (matchMode == MatchMode.PathPrefixAndSuffixWildcard)) ^ string.IsNullOrEmpty(pathWildcard));
            Contract.Requires(!((matchMode == MatchMode.PathPrefixWildcard) || (matchMode == MatchMode.PathSuffixWildcard) || (matchMode == MatchMode.PathPrefixAndSuffixWildcard)) ^ !path.IsValid);

            m_path = path;
            m_matchMode = matchMode;
            m_pathWildcardUpper = pathWildcard?.ToUpperInvariant();
            PathFromMount = pathFromMount;
        }

        /// <inheritdoc/>
        protected override int GetDerivedSpecificHashCode()
        {
            return HashCodeHelper.Combine(
                m_path.GetHashCode(),
                m_matchMode.GetHashCode(),
                m_pathWildcardUpper?.GetHashCode() ?? 0,
                PathFromMount.GetHashCode());
        }

        /// <inheritdoc/>
        public override bool CanonicallyEquals(PipFilter pipFilter)
        {
            PathBasedFilter pathBasedFilter;
            return (pathBasedFilter = pipFilter as PathBasedFilter) != null &&
                   m_path == pathBasedFilter.m_path &&
                   m_matchMode == pathBasedFilter.m_matchMode &&
                   m_pathWildcardUpper == pathBasedFilter.m_pathWildcardUpper &&
                   PathFromMount == pathBasedFilter.PathFromMount;
        }

        /// <summary>
        /// Checks if a path matches the filter
        /// </summary>
        internal bool PathMatches(AbsolutePath path, PathTable pathTable)
        {
            switch (m_matchMode)
            {
                case MatchMode.FilePath:
                    return path.Equals(m_path);
                case MatchMode.WithinDirectory:
                    return path.GetParent(pathTable).Equals(m_path);
                case MatchMode.WithinDirectoryAndSubdirectories:
                    return path.IsWithin(pathTable, m_path);
                case MatchMode.PathPrefixWildcard:
                    return path.ToString(pathTable).EndsWith(m_pathWildcardUpper, StringComparison.OrdinalIgnoreCase);
                case MatchMode.PathSuffixWildcard:
                    return path.ToString(pathTable).StartsWith(m_pathWildcardUpper, StringComparison.OrdinalIgnoreCase);
                case MatchMode.PathPrefixAndSuffixWildcard:
                    return path.ToString(pathTable).ToUpperInvariant().Contains(m_pathWildcardUpper);
                default:
                    throw Contract.AssertFailure("Unknown Match Mode");
            }
        }

        /// <summary>
        /// Checks if a directory path matches the filter
        /// </summary>
        internal bool DirectoryPathMatches(AbsolutePath path, bool topLevelOnly, PathTable pathTable)
        {
            switch (m_matchMode)
            {
                case MatchMode.FilePath:
                    {
                        // Filter: Z:\a\b\c\d
                        // Path:
                        // - Z:\a\b\c\d - OK
                        // - Z:\a\b\c - OK
                        // - Z:\a\b - OK if topLevelOnly is false.
                        if (m_path == path)
                        {
                            return true;
                        }

                        return topLevelOnly ? m_path.GetParent(pathTable) == path : m_path.IsWithin(pathTable, path);
                    }

                case MatchMode.WithinDirectory:
                    return path.GetParent(pathTable).Equals(m_path);
                case MatchMode.WithinDirectoryAndSubdirectories:
                    return path.IsWithin(pathTable, m_path);
                case MatchMode.PathPrefixWildcard:
                    {
                        // Filter: *\c\d
                        // Path:
                        // - Z:\a\b\c\d - OK
                        // - Z:\p\q - Not OK, although there can be the file Z:\p\q\c\d
                        // To be conservative, every path matches the path prefix wildcard, but the filtering would be too coarse.
                        return path.ToString(pathTable).EndsWith(m_pathWildcardUpper, StringComparison.OrdinalIgnoreCase);
                    }

                case MatchMode.PathSuffixWildcard:
                    {
                        // Filter: Z:\a\b\c\d\*
                        // Path:
                        // - Z:\a\b\c\d - OK
                        // - Z:\a\b\c\d\e - OK
                        // - Z:\a\b\c - OK if topLevelOnly is false.
                        var pathStr = path.ToString(pathTable);
                        if (pathStr.StartsWith(m_pathWildcardUpper, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        pathStr += Path.DirectorySeparatorChar.ToString();
                        if (!m_pathWildcardUpper.StartsWith(pathStr, StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }

                        return !topLevelOnly || m_pathWildcardUpper.IndexOf(Path.DirectorySeparatorChar.ToString(), pathStr.Length, StringComparison.OrdinalIgnoreCase) == -1;
                    }

                case MatchMode.PathPrefixAndSuffixWildcard:
                    return path.ToString(pathTable).ToUpperInvariant().Contains(m_pathWildcardUpper);
                default:
                    throw Contract.AssertFailure("Unknown Match Mode");
            }
        }

        protected AbsolutePath GetSpecRootToResolve()
        {
            if (m_matchMode == MatchMode.FilePath ||
                m_matchMode == MatchMode.WithinDirectory ||
                m_matchMode == MatchMode.WithinDirectoryAndSubdirectories)
            {
                return m_path;
            }

            return AbsolutePath.Invalid;
        }
    }
}
