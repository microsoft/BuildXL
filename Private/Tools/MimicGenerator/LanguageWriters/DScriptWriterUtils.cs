// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;

namespace Tool.MimicGenerator.LanguageWriters
{
    internal enum PathType
    {
        Path,
        Directory,
        File,
    }

    internal static class DScriptWriterUtils
    {
        /// <summary>
        /// It is a string expression if it is enclosed in single or double quotes.
        /// </summary>
        internal static bool IsStringExpression(string s)
        {
            if (s == null)
            {
                return false;
            }

            s = s.Trim();
            return (s.StartsWith("'", StringComparison.Ordinal) && s.EndsWith("'", StringComparison.Ordinal)) ||
                   (s.StartsWith("\"", StringComparison.Ordinal) && s.EndsWith("\"", StringComparison.Ordinal));
        }

        /// <summary>
        /// The DScript syntax for paths is p``, d``, f``, so if 's' is a string expression
        /// (i.e., quoted), it first unquotes it, then, in any case, encloses it in _` and `.
        /// </summary>
        internal static string ToPath(string s, PathType type)
        {
            Contract.Requires(s != null);

            bool isStringExpr = IsStringExpression(s);
            string unquoted = isStringExpr ? s.Substring(1, s.Length - 2) : s;
            string escaped = unquoted.Replace("'", "\'").Replace("`", "\\`");

            // Switch the path to a unix style
            escaped = escaped.Replace('\\', '/');

            string prefix = string.Empty;
            switch (type)
            {
                case PathType.Directory:
                    prefix = "d`";
                    break;
                case PathType.File:
                    prefix = "f`";
                    break;
                case PathType.Path:
                    prefix = "p`";
                    break;
            }

            return prefix + escaped + "`";
        }

        /// <nodoc/>
        internal static string EncloseInQuotes(string s, string quoteMark)
        {
            return quoteMark + s + quoteMark;
        }

        /// <nodoc/>
        internal static string EncloseInDoubleQuotes(string s)
        {
            return EncloseInQuotes(s, "\"");
        }

        /// <summary>
        /// Takes 2 path, 'target' and 'from', and returns a string representing the relative path
        /// from 'from' to 'target'.  If no common prefix is found, no relative path can be constructed,
        /// so 'target' is returned instead.
        /// </summary>
        internal static string ToRelativePath(string target, string from)
        {
            Contract.Requires(target != null);
            Contract.Requires(from != null);

            string[] targetDirSegments = NormalizePath(Path.GetDirectoryName(target)).Split('/');
            string[] fromDirSegments = NormalizePath(Path.GetDirectoryName(from)).Split('/');

            // find the common prefix
            int cnt = 0;
            while (cnt < targetDirSegments.Length && cnt < fromDirSegments.Length && targetDirSegments[cnt].Equals(fromDirSegments[cnt]))
            {
                cnt++;
            }

            // no common prefix found -> return target
            if (cnt == 0)
            {
                return target;
            }

            // prefix found ->
            //   1. navigate up the remainder fo 'fromDirSegments' (using '..')
            int relPathCnt = 0;
            string[] relPath = new string[(fromDirSegments.Length - cnt) + (targetDirSegments.Length - cnt) + 1];
            for (int i = cnt; i < fromDirSegments.Length; i++)
            {
                relPath[relPathCnt++] = "..";
            }

            // 2. then down the remainder of 'targetDirSegments' (using those exact segements)
            for (int i = cnt; i < targetDirSegments.Length; i++)
            {
                relPath[relPathCnt++] = targetDirSegments[i];
            }

            // 3. finally add the target file name
            relPath[relPathCnt] = Path.GetFileName(target);
            return string.Join("/", relPath);
        }

        /// <summary>
        /// Return relative path (to the package location) instead of Path.Combine expression
        /// </summary>
        internal static string GetPathFromExpression(string path)
        {
            Contract.Requires(path != null);
            Contract.Requires(path.Length >= 3);

            var driveLetter = char.ToUpperInvariant(path[0]);
            path = NormalizePath(path).Replace("'", @"\'");
            return "/" + driveLetter + "/" + path.Remove(0, 3);
        }

        /// <summary>
        /// Returns full file path without the file extension.
        /// </summary>
        internal static string RemoveFileEnding(string path)
        {
            return Path.GetDirectoryName(path) + Path.DirectorySeparatorChar + Path.GetFileNameWithoutExtension(path);
        }

        /// <summary>
        /// Replaces backslashes (\) with forwardslashes (/).
        /// </summary>
        internal static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }
    }
}
