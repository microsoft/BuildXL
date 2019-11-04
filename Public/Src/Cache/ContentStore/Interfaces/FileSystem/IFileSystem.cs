// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#nullable enable

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    ///     A thread-safe file system abstraction to enable multiple implementations and testing.
    /// </summary>
    public interface IFileSystem<in T> : IReadableFileSystem<T>
        where T : PathBase
    {
        /// <summary>
        ///     Create a new directory.
        /// </summary>
        /// <param name="path">Path to new directory</param>
        /// <remarks>
        ///     An exception is thrown on error.
        /// </remarks>
        void CreateDirectory(T path);

        /// <summary>
        ///     Delete an existing directory.
        /// </summary>
        /// <param name="path">Path to directory</param>
        /// <param name="deleteOptions">Options for delete operation which can be null to use defaults</param>
        /// <remarks>
        ///     Default behavior is to not recurse and not delete read-only files.
        ///     An exception is thrown on error.
        /// </remarks>
        void DeleteDirectory(T path, DeleteOptions deleteOptions);

        /// <summary>
        ///     Delete an existing file.
        /// </summary>
        /// <param name="path">Path to file</param>
        /// <remarks>
        ///     No op if the file does not exist.
        /// </remarks>
        void DeleteFile(T path);

        /// <summary>
        ///     Write the contents of a new file or overwrite the contents of an existing file.
        /// </summary>
        /// <param name="path">Path to the file.</param>
        /// <param name="content">Contents to replace any existing content.</param>
        void WriteAllBytes(T path, byte[] content);

        /// <summary>
        ///     Move a file from one location to another.
        /// </summary>
        /// <param name="sourceFilePath">Path to source file.</param>
        /// <param name="destinationFilePath">Path to destination file.</param>
        /// <param name="replaceExisting">Replace a file if it already exists at the destination path.</param>
        void MoveFile(T sourceFilePath, T destinationFilePath, bool replaceExisting);

        /// <summary>
        ///     Rename a directory.
        /// </summary>
        /// <param name="sourcePath">Path to existing directory.</param>
        /// <param name="destinationPath">Path to new, not yet existing directory.</param>
        void MoveDirectory(T sourcePath, T destinationPath);
    }
}
