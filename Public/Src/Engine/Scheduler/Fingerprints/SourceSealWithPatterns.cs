// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler.Fingerprints
{
    /// <summary>
    /// Source seal directory path with list of wildcard patterns
    /// </summary>
    internal readonly struct SourceSealWithPatterns
    {
        /// <summary>
        /// Source seal directory path
        /// </summary>
        public readonly AbsolutePath Path;

        /// <summary>
        /// Wildcard patterns that filter the contents of the source seal directory
        /// </summary>
        public readonly ReadOnlyArray<StringId> Patterns;

        /// <summary>
        /// Constructor
        /// </summary>
        public SourceSealWithPatterns(AbsolutePath path, ReadOnlyArray<StringId> patterns)
        {
            Contract.Requires(path.IsValid);

            Path = path;
            Patterns = patterns;
        }

        /// <summary>
        /// Check whether the source seal directory contains the given child path based on the wildcard patterns
        /// </summary>
        public bool Contains(PathTable pathTable, AbsolutePath childPath, bool isTopDirectoryOnly = true)
        {
#if PLATFORM_WIN
            Contract.Requires(pathTable != null);
            Contract.Requires(childPath.IsValid);                
#else
            if (pathTable == null || !childPath.IsValid)
            {
                return false;
            }
#endif

            bool checkPatterns = false;
            if (isTopDirectoryOnly && childPath.GetParent(pathTable) == Path)
            {
                checkPatterns = true;
            }

            if (!isTopDirectoryOnly && childPath != Path && childPath.IsWithin(pathTable, Path))
            {
                checkPatterns = true;
            }

            if (!checkPatterns)
            {
                return false;
            }

            if (!Patterns.IsValid || Patterns.Length == 0)
            {
                return true;
            }

            var childPathName = childPath.GetName(pathTable).ToString(pathTable.StringTable);
            foreach (var pattern in Patterns)
            {
                var patternStr = pattern.ToString(pathTable.StringTable);

                // As this is potentially a hot path in the ObservedInputProcessor, we should replace it 
                // with SingleWildcardPatternMatch in case of a perf regression.
                if (FileUtilities.PathMatchPattern(childPathName, patternStr))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks whether the filename passes the given wildcard pattern
        /// </summary>
        /// <remarks>
        /// This is intentionally non-standard wildcard pattern matching. 
        /// Only one wildcard char, '*' is allowed and '?' is not supported.
        /// This method is at least 3 times faster than 'PathMatchSpecW' from 'shlwapi.dll'
        /// </remarks>
        public static bool SingleWildcardPatternMatch(string name, string pattern)
        {
            Contract.Requires(name != null);
            Contract.Requires(!string.IsNullOrEmpty(pattern));

            // p is an index for pattern
            // n is an index for name
            int n = 0;
            for (int p = 0; p < pattern.Length; p++)
            {
                char patternChar = pattern[p];

                if (patternChar == '*')
                {
                    int patternRemainderLength = pattern.Length - p - 1;
                    n = name.Length - patternRemainderLength;
                }
                else if (patternChar == '?')
                {
                    Contract.Assert(false, "Question mark is not allowed in source seal wildcard patterns");
                }
                else if (n >= 0 && n < name.Length)
                {
                    if (patternChar.ToUpperInvariantFast() != name[n].ToUpperInvariantFast())
                    {
                        return false;
                    }

                    n++;
                }
                else
                {
                    return false;
                }
            }

            if (name.Length != n)
            {
                return false;
            }

            return true;
        }
    }
}
