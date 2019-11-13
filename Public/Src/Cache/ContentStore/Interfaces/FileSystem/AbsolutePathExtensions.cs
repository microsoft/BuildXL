// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;
#nullable enable

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    /// Extension methods for <see cref="AbsolutePath"/> class.
    /// </summary>
    public static class AbsolutePathExtensions
    {
        /// <summary>
        /// Gets the file name for a given <paramref name="path"/>.
        /// </summary>
        public static string GetFileName(this AbsolutePath path)
        {
            string fileName = Path.GetFileName(path.Path);
            Contract.AssertNotNull(fileName);
            return fileName;
        }
    }
}
