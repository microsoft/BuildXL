// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;

namespace BuildXL.FrontEnd.Script.Util
{
    /// <summary>
    /// Utilities for paths.
    /// </summary>
    public static class PathUtil
    {
        /// <summary>
        /// Normalizes path.
        /// </summary>
        public static string NormalizePath(string path)
        {
            Contract.Requires(path != null);

            return path.Replace('\\', '/');
        }
    }
}
