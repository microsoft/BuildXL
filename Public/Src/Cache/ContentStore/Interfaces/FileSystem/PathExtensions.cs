// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
            Contract.Requires(path != null);
            Contract.Requires(sourceRoot != null);
            Contract.Requires(destinationRoot != null);

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
