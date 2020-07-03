// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    ///     Extension methods for PathBase and its derived classes.
    /// </summary>
    public static class PathExtensions
    {
        /// <summary>
        ///     Replace a leading part of a path.
        /// </summary>
        public static AbsolutePath SwapRoot(this AbsolutePath path, AbsolutePath sourceRoot, AbsolutePath destinationRoot)
        {
            Contract.RequiresNotNull(path);
            Contract.RequiresNotNull(sourceRoot);
            Contract.RequiresNotNull(destinationRoot);

            var x = path.Path.IndexOf(sourceRoot.Path, StringComparison.OrdinalIgnoreCase);
            if (x < 0)
            {
                return path;
            }

            if (sourceRoot.Length == path.Length)
            {
                return destinationRoot;
            }

            return destinationRoot / path.Path.Substring(x + sourceRoot.Length + 1);
        }
    }
}
