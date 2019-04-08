// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    ///     Readable filesystem abstraction to enable multiple implementations and testing
    /// </summary>
    public interface IReadableFileSystem<in T>
        where T : PathBase
    {
        /// <summary>
        ///     enumerates the files (does not include directories) underneath the input directory path
        /// </summary>
        /// <param name="path">Path to directory</param>
        /// <param name="options">options in how to enumerate; can be null to use defaults</param>
        /// <returns>the set of files matching the enumerate options under the path</returns>
        IEnumerable<FileInfo> EnumerateFiles(T path, EnumerateOptions options);

        /// <summary>
        ///     Check if named directory exists.
        /// </summary>
        /// <param name="path">Path to directory</param>
        /// <returns>true if directory exists; false otherwise.</returns>
        bool DirectoryExists(T path);

        /// <summary>
        ///     Check if named file exists.
        /// </summary>
        /// <param name="path">Path to file</param>
        /// <returns>true if file exists; false otherwise.</returns>
        bool FileExists(T path);

        /// <summary>
        ///     Read the contents of an existing file.
        /// </summary>
        /// <param name="path">Path to the file.</param>
        /// <returns>All contents of the existing file.</returns>
        byte[] ReadAllBytes(T path);

        /// <summary>
        /// Creates a stream to an existing file.
        /// Note that the permissions are such that it is readable, but not
        /// writable by the caller.
        /// </summary>
        /// <param name="path">The path to open a stream to.</param>
        /// <param name="share">the file sharing permissions for the given path</param>
        /// <returns>A stream to the file that is requested</returns>
        Task<Stream> OpenReadOnlyAsync(T path, FileShare share);
    }
}
