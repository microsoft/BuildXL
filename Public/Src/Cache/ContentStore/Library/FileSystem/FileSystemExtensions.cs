// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using FileInfo = BuildXL.Cache.ContentStore.Interfaces.FileSystem.FileInfo;

namespace BuildXL.Cache.ContentStore.FileSystem
{
    /// <summary>
    ///     Extension methods for IFileSystem and its derived interfaces.
    /// </summary>
    public static class FileSystemExtensions
    {
        /// <summary>
        ///     Remove all contents, subdirectories and files, from a directory.
        /// </summary>
        public static void ClearDirectory(this IAbsFileSystem fileSystem, AbsolutePath path, DeleteOptions deleteOptions)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(path != null);

            if (fileSystem.FileExists(path))
            {
                throw new IOException("not a directory");
            }

            var subDirectories = fileSystem.EnumerateDirectories(path, EnumerateOptions.None);

            if ((deleteOptions & DeleteOptions.Recurse) != 0)
            {
                foreach (AbsolutePath directoryPath in subDirectories)
                {
                    fileSystem.DeleteDirectory(directoryPath, deleteOptions);
                }
            }
            else if (subDirectories.Any())
            {
                throw new IOException("subdirectories not deleted without recurse option");
            }

            foreach (FileInfo fileInfo in fileSystem.EnumerateFiles(path, EnumerateOptions.None))
            {
                fileSystem.DeleteFile(fileInfo.FullPath);
            }
        }
    }
}
