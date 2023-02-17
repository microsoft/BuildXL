// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

namespace BuildXL.FrontEnd.Sdk.FileSystem
{
    /// <summary>
    /// A filesystem abstraction that also allows creation of files.
    /// </summary>
    public interface IMutableFileSystem : IFileSystem
    {
        /// <summary>
        /// Writes all text to the given path.
        /// </summary>
        IMutableFileSystem WriteAllText(string path, string content);

        /// <summary>
        /// Writes all text to the given path.
        /// </summary>
        IMutableFileSystem WriteAllText(AbsolutePath path, string content);

        /// <summary>
        /// Creates a directory.
        /// </summary>
        IMutableFileSystem CreateDirectory(AbsolutePath path);
    }
}
