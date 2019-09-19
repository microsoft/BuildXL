// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Sdk.FileSystem
{
    /// <summary>
    /// Provides an abstraction layer to get to a file content from a path and other file-system-related operations
    /// </summary>
    public interface IFileSystem
    {
        /// <summary>
        /// When the engine reloads it swaps out the PathTable.
        /// Implementors must returns a new FileSystem of the same type and remap any AbsolutePaths that were
        /// stored inside to the new PathTable.
        /// </summary>
        IFileSystem CopyWithNewPathTable(PathTable pathTable);

        /// <summary>
        /// Opens a file under <param name="path"/> for text reading with a particular encoding.
        /// </summary>
        /// <remarks>
        /// The path must point to an existing file
        /// </remarks>
        [JetBrains.Annotations.NotNull]
        StreamReader OpenText(AbsolutePath path);

        /// <summary>
        /// Whether <param name="path"/> points to an existing file.
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        bool Exists(AbsolutePath path);

        /// <summary>
        /// Returns if <param name="path"/> points to a directory
        /// </summary>
        /// <remarks>
        /// The path must point to an existing file
        /// </remarks>
        [System.Diagnostics.Contracts.Pure]
        bool IsDirectory(AbsolutePath path);

        /// <summary>
        /// Returns the name of the file or directory under <param name="path"/>
        /// </summary>
        /// <remarks>
        /// The path must point to an existing file or directory.
        /// </remarks>
        [System.Diagnostics.Contracts.Pure]
        [JetBrains.Annotations.NotNull]
        string GetBaseName(AbsolutePath path);

        /// <summary>
        /// Path table where the AbsolutePaths handled by this file system are based on
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        [JetBrains.Annotations.NotNull]
        PathTable GetPathTable();

        /// <summary>
        /// Returns an enumerable collection of directories that match a specified pattern in the specified path.
        /// </summary>
        /// <param name="path">Path to the directory to search.</param>
        /// <param name="pattern">The search string to match against the names of directories (allows path literals, wildcards * and ?, but not regexes).</param>
        /// <param name="recursive">Whether to search recursively.</param>
        [JetBrains.Annotations.NotNull]
        IEnumerable<AbsolutePath> EnumerateDirectories(AbsolutePath path, [JetBrains.Annotations.NotNull]string pattern = "*", bool recursive = false);

        /// <summary>
        /// Returns an enumerable collection of files that match a specified pattern in the specified path.
        /// </summary>
        /// <param name="path">Path to the directory to search.</param>
        /// <param name="pattern">The search string to match against the names of files (allows path literals, wildcards * and ?, but not regexes).</param>
        /// <param name="recursive">Whether to search recursively.</param>
        [JetBrains.Annotations.NotNull]
        IEnumerable<AbsolutePath> EnumerateFiles(AbsolutePath path, [JetBrains.Annotations.NotNull]string pattern = "*", bool recursive = false);

        /// <nodoc/>
        EnumerateDirectoryResult EnumerateDirectoryEntries(
            string directoryPath,
            bool enumerateDirectory,
            string pattern,
            uint directoriesToSkipRecursively,
            bool recursive,
            IDirectoryEntriesAccumulator accumulators);
    }

    /// <nodoc/>
    public static class FileSystemExtensionMethods
    {
        /// <summary>
        /// Whether <param name="path"/> points to an existing artifact in the <param name="fileSystem"/>, and the artifact is a directory
        /// </summary>
        public static bool DirectoryExists(this IFileSystem fileSystem, AbsolutePath path)
        {
            return fileSystem.Exists(path) && fileSystem.IsDirectory(path);
        }
    }
}
