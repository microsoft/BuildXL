// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using FileInfo = BuildXL.Cache.ContentStore.Interfaces.FileSystem.FileInfo;

namespace BuildXL.Cache.ContentStore.FileSystem
{
    /// <summary>
    ///     Extension methods for IFileSystem and its derived interfaces.
    /// </summary>
    public static class FileSystemExtensions
    {
        private static readonly Tracer Tracer = new Tracer(nameof(FileSystemExtensions));
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

        /// <summary>
        /// Tries deleting a given <paramref name="path"/> and traces an exception in case of an error.
        /// </summary>
        public static bool TryDeleteFile(this IAbsFileSystem fileSystem, Context context, AbsolutePath path)
        {
            try
            {
                fileSystem.DeleteFile(path);
                return true;
            }
            catch (Exception e)
            {
                Tracer.Warning(context, e, $"Failed to delete file '{path}'");
                return false;
            }
        }
    }
}
