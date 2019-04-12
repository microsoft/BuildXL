// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge
{
    /// <summary>
    /// Helper class that converts path string to path instance.
    /// TODO: add more detailed error messages when paths don't look as expected and propagate message upstream.
    /// </summary>
    internal sealed class PathResolver
    {
        private const char DirectorySeparatorChar = '\\';
        private const char AltDirectorySeparatorChar = '/';
        private const char VolumeSeparatorChar = ':';

        private readonly PathTable m_pathTable;

        public PathResolver(PathTable pathTable)
        {
            Contract.Requires(pathTable != null);

            m_pathTable = pathTable;
        }

        /// <summary>
        /// Return true if <paramref name="value"/> is a regular path and not a path atom or path fragment.
        /// </summary>
        [Pure]
        public static bool IsRegularPath(StringSegment value)
        {
            return !IsPathAtom(value) && !IsPathFragment(value);
        }

        /// <summary>
        /// Parses path literal value to <see cref="ParsedPath"/> instance.
        /// </summary>
        /// <remarks>
        /// <paramref name="value"/> should not have single-quotes, or any prefixes. This should be just a value of the path literal.
        /// </remarks>
        public ParsedPath ParseRegularPath(StringSegment value)
        {
            Contract.Requires(IsRegularPath(value));
            return ParseAbsoluteOrRelativePath(value, isPathFragment: false);
        }

        /// <summary>
        /// Returns true if <paramref name="value"/> starts with @
        /// </summary>
        [Pure]
        public static bool IsPathAtom(StringSegment value)
        {
            if (value.Length == 0)
            {
                return false;
            }

            return value[0] == Names.PathAtomMarker;
        }

        /// <summary>
        /// Parses specified <paramref name="value"/>.
        /// </summary>
        public PathAtom ParsePathAtom(StringSegment value)
        {
            Contract.Requires(IsPathAtom(value));

            // Just skipping marker symbol
            var pathAtomText = value.Subsegment(1, value.Length - 1);

            PathAtom.TryCreate(m_pathTable.StringTable, pathAtomText, out PathAtom atom);
            return atom;
        }

        /// <summary>
        /// Returns true if <paramref name="value"/> starts with #
        /// </summary>
        [Pure]
        public static bool IsPathFragment(StringSegment value)
        {
            if (value.Length == 0)
            {
                return false;
            }

            return value[0] == Names.PathFragmentMarker;
        }

        /// <summary>
        /// Parses specified <paramref name="value"/> as a path fragment.
        /// </summary>
        public RelativePath ParsePathFragment(string value)
        {
            Contract.Requires(IsPathFragment(value));

            // Need to remove path fragment marker!
            var pathValue = value.Substring(1);

            var result = ParseAbsoluteOrRelativePath(pathValue, isPathFragment: true);
            if (!result.IsValid)
            {
                return RelativePath.Invalid;
            }

            // Path fragments could be only relative paths!
            Contract.Assert(result.IsFileRelative);
            return result.FileRelative;
        }

        private ParsedPath ParseAbsoluteOrRelativePath(StringSegment fragment, bool isPathFragment)
        {
            if (fragment.Length == 0)
            {
                return ParsedPath.Invalid();
            }

            // If we're running on Unix skip all of this, because absolute paths are always prefixed with '/'
            if (fragment[0] == '/' && !OperatingSystemHelper.IsUnixOS)
            {
                if (isPathFragment)
                {
                    // Relative paths can't start with '/'
                    return ParsedPath.Invalid();
                }

                if (fragment.Length < 2)
                {
                    // Path '/' is invalid.
                    return ParsedPath.Invalid();
                }

                if (fragment[1] != '/' && fragment[1] != '?')
                {
                    // Case: '/f/g/h'.
                    var relativePath = fragment.Subsegment(1, fragment.Length - 1);

                    if (!RelativePath.TryCreate(m_pathTable.StringTable, relativePath, out RelativePath part))
                    {
                        return ParsedPath.Invalid();
                    }

                    // Contract.Assert(m_rootPath.IsValid, "To get absolute path root path should be valid");
                    return ParsedPath.PackageRelativePath(part, m_pathTable);
                }
            }

            try
            {
                if (IsPathRooted(fragment))
                {
                    if (isPathFragment)
                    {
                        // Relative paths can't be rooted.
                        return ParsedPath.Invalid();
                    }

                    // Case: 'c:/f/g/h'.
                    if (!AbsolutePath.TryCreate(m_pathTable, fragment, out AbsolutePath absolutePath))
                    {
                        return ParsedPath.Invalid();
                    }

                    return ParsedPath.AbsolutePath(absolutePath, m_pathTable);
                }
            }
            catch (ArgumentException)
            {
                return ParsedPath.Invalid();
            }

            // Case: 'f/g/h', or '../f/g/h', or './f/g/h'.
            if (isPathFragment)
            {
                // .. is invalid in path fragments
                if (!RelativePath.TryCreate(m_pathTable.StringTable, fragment, out RelativePath part))
                {
                    return ParsedPath.Invalid();
                }

                return ParsedPath.FileRelativePath(part, 0, m_pathTable);
            }

            int index = 0;
            int parentCount = 0;

            while (CheckAndUpdatePathFragment(fragment, ref index, out bool getParent))
            {
                if (getParent)
                {
                    parentCount++;
                }
            }

            var relativePathFragment = fragment;

            if (index == 0)
            {
                // Do nothing.
            }
            else if (index >= fragment.Length)
            {
                relativePathFragment = ".";
            }
            else
            {
                relativePathFragment = fragment.Subsegment(index, fragment.Length - index);
            }

            if (!RelativePath.TryCreate(m_pathTable.StringTable, relativePathFragment, out RelativePath relativePart))
            {
                return ParsedPath.Invalid();
            }

            return ParsedPath.FileRelativePath(relativePart, parentCount, m_pathTable);
        }

        public static bool IsPathRooted(StringSegment path)
        {
            if (path.Length != 0)
            {
                int length = path.Length;
                if ((length >= 1 && (path[0] == DirectorySeparatorChar || path[0] == AltDirectorySeparatorChar)) ||
                    (length >= 2 && path[1] == VolumeSeparatorChar))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CheckAndUpdatePathFragment(StringSegment fragment, ref int index, out bool getParent)
        {
            Contract.Requires(fragment.Length != 0);

            getParent = false;

            if (index >= fragment.Length)
            {
                return false;
            }

            if (index <= fragment.Length - 1)
            {
                if (fragment[index] == '.')
                {
                    if (index <= fragment.Length - 2)
                    {
                        if (fragment[index + 1] == '/')
                        {
                            // Case: './abc/def'
                            index += 2;
                            return true;
                        }

                        if (fragment[index + 1] == '.')
                        {
                            if (index <= fragment.Length - 3)
                            {
                                if (fragment[index + 2] == '/')
                                {
                                    // Case: '../'
                                    getParent = true;
                                    index += 3;
                                    return true;
                                }
                            }
                            else
                            {
                                // Case: '..'
                                getParent = true;
                                index += 2;
                                return true;
                            }
                        }
                        else
                        {
                            // Case: '.abc/def'
                            return false;
                        }
                    }
                    else
                    {
                        // Case: '.'
                        ++index;
                        return true;
                    }
                }
                else
                {
                    // Case: 'abc/def'
                    return false;
                }
            }

            return false;
        }
    }
}
