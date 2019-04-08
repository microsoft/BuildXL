// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace Microsoft.Build.Prediction
{
    /// <summary>
    /// Gets the appdomain-wide appropriate filesystem path string comparer
    /// appropriate to the current operating system.
    /// </summary>
    internal static class PathComparer
    {
        public static readonly StringComparer Instance = GetPathComparer();
        public static readonly StringComparison Comparison = GetPathComparison();

        private static StringComparer GetPathComparer()
        {
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.MacOSX:
                case PlatformID.Unix:
                    return StringComparer.Ordinal;
                default:
                    return StringComparer.OrdinalIgnoreCase;
            }
        }

        private static StringComparison GetPathComparison()
        {
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.MacOSX:
                case PlatformID.Unix:
                    return StringComparison.Ordinal;
                default:
                    return StringComparison.OrdinalIgnoreCase;
            }
        }
    }
}
