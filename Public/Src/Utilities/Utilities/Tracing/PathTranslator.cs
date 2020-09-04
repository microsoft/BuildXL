// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Translates string paths from one root to another.
    /// </summary>
    /// <remarks>
    /// This translator is used only for logging purposes. One use of this translator is to handle substed paths. For example,
    /// in our self-host, we subst BuildXL source folder to B:\, and thus, all reported paths are in terms of B:\. However, users
    /// may not be aware of this subst. Hence, the reported paths need to be translated into the real path.
    /// </remarks>
    public sealed class PathTranslator
    {
        private readonly ObjectPool<List<int>> m_listPool;

        /// <summary>
        /// From path.
        /// </summary>
        public string FromPath { get; }

        /// <summary>
        /// To path.
        /// </summary>
        public string ToPath { get; }

        // Set of characters to allow as prefixes to denote a path. A sequence matching fromPath must be preceded
        // by one of these characters or be at the beginning of the string in order to be translated.
        private static readonly char[] s_prefixCharacters = { '\'', '|', ':', '"', '[', ']' };
        private static readonly string[] s_prefixPatterns = { @"\\?\", @"\??\" };

        public static bool CreateIfEnabled(AbsolutePath from, AbsolutePath to, PathTable pathTable, out PathTranslator translator)
        {
            translator = null;
            if (!from.IsValid || !to.IsValid)
            {
                return false;
            }

            translator = new PathTranslator(from.ToString(pathTable), to.ToString(pathTable));
            return true;
        }

        public static bool CreateIfEnabled(string from, string to, out PathTranslator translator)
        {
            translator = null;
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            {
                return false;
            }

            translator = new PathTranslator(from, to);
            return true;
        }

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="fromPath">Root to translate paths from</param>
        /// <param name="toPath">Root to translate paths to</param>
        public PathTranslator(string fromPath, string toPath)
        {
            Contract.RequiresNotNull(fromPath);
            Contract.RequiresNotNull(toPath);

            string suffix = Path.DirectorySeparatorChar + string.Empty;
            FromPath = fromPath.EndsWith(suffix, StringComparison.Ordinal) ? fromPath : fromPath + suffix;
            ToPath = toPath.EndsWith(suffix, StringComparison.Ordinal) ? toPath : toPath + suffix;
            m_listPool = new ObjectPool<List<int>>(() => new List<int>(), (list) => list.Clear());
        }

        /// <summary>
        /// Returns a <see cref="PathTranslator"/> that performs the inverse translation.
        /// </summary>
        public PathTranslator GetInverse() => new PathTranslator(ToPath, FromPath);

        /// <summary>
        /// Translates any paths in the AbsolutePath based on the configuration of <see cref="PathTranslator"/>
        /// </summary>
        public AbsolutePath Translate(PathTable table, AbsolutePath path)
        {
            var pathStr = path.ToString(table);
            var translatedPathStr = Translate(pathStr);
            return AbsolutePath.Create(table, translatedPathStr);
        }

        /// <summary>
        /// Translates any paths in the string based on the configuration of <see cref="PathTranslator"/>
        /// </summary>
        public string Translate(string text)
        {
            using (var pool = m_listPool.GetInstance())
            {
                List<int> matches = pool.Instance;

                // First, find all indexes of the path to remap
                int startChar = 0;
                while (startChar < text.Length)
                {
                    int found = text.IndexOf(FromPath, startChar, OperatingSystemHelper.PathComparison);
                    if (found == -1)
                    {
                        break;
                    }
                    else
                    {
                        // Mark that we've searched up through this point
                        startChar = found + FromPath.Length;

                        // Do some validation on the previous character to make sure we have a path looking thing
                        if (found == 0)
                        {
                            matches.Add(found);
                        }
                        else
                        {
                            char previousChar = text[found - 1];
                            if (char.IsWhiteSpace(previousChar))
                            {
                                matches.Add(found);
                                continue;
                            }

                            bool added = false;

                            foreach (char allowedPrefix in s_prefixCharacters)
                            {
                                if (previousChar == allowedPrefix)
                                {
                                    matches.Add(found);
                                    added = true;
                                    break;
                                }
                            }

                            if (added)
                            {
                                continue;
                            }

                            foreach (string prefixPattern in s_prefixPatterns)
                            {
                                if (found >= prefixPattern.Length &&
                                    text.IndexOf(prefixPattern, found - prefixPattern.Length) == found - prefixPattern.Length)
                                {
                                    matches.Add(found);
                                    break;
                                }
                            }

                            // We matched the fromPath, but no allowed prefix character. Don't replace the string
                        }
                    }
                }

                if (matches.Count > 0)
                {
                    using (var sbPool = Pools.StringBuilderPool.GetInstance())
                    {
                        StringBuilder sb = sbPool.Instance;

                        int start = 0;
                        foreach (var index in matches)
                        {
                            // Add everything up until this instance
                            sb.Append(text, start, index - start);
                            start += index - start;

                            // Add the replaced text
                            sb.Append(ToPath);

                            // skip past the fromPath for the next addition
                            start += FromPath.Length;
                        }

                        // Add the end of the string
                        if (start < text.Length)
                        {
                            sb.Append(text, start, text.Length - start);
                        }

                        return sb.ToString();
                    }
                }

                return text;
            }
        }
    }
}
