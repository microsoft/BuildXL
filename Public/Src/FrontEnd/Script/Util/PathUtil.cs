// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
