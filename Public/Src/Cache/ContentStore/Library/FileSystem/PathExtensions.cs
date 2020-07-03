// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

namespace BuildXL.Cache.ContentStore.FileSystem
{
    /// <summary>
    ///     Extension methods for PathBase and its derived classes.
    /// </summary>
    public static class PathExtensions
    {
        /// <summary>
        ///     Build a string from the path with special characters removed/replaced.
        /// </summary>
        /// <remarks>
        ///     The result can be used to name system-wide resources like mutexes, pipes, etc.
        /// </remarks>
        public static string ToGlobalResourceName(this PathBase path)
        {
            // Names for Mutexs can not have any slashes - need to skip "/" for unix
            return @"Global\" + path.Path.ToUpperInvariant().Replace(":", string.Empty).Replace('\\', '_').Replace('/', '_');
        }
    }
}
