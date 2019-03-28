// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;

namespace BuildXL.Utilities
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
