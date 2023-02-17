// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.IO;

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// Static helper class to format path
    /// </summary>
    public static class PathFormatter
    {
        private static readonly char s_hostOsSeparator = Path.DirectorySeparatorChar;

        /// <summary>
        /// Returns the separator for the given pathformat
        /// </summary>
        public static char GetPathSeparator(PathFormat pathFormat)
        {
            switch (pathFormat)
            {
                case PathFormat.HostOs:
                    return s_hostOsSeparator;
                case PathFormat.Script:
                    return '/';
                case PathFormat.Windows:
                    return '\\';
                case PathFormat.Unix:
                    return '/';
                default:
                    Contract.Assert(false, "Unexpected pathFormat");
                    return '\0';
            }
        }
    }
}
